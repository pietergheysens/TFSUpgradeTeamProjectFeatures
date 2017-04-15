using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Server;
using Microsoft.TeamFoundation.Server.WebAccess.WorkItemTracking.Common;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TFSUpgradeTeamProjectFeatures
{
    class Program
    {
        static void Main(string[] args)
        {
            string rootLogFolder = ConfigurationManager.AppSettings["RootLogFolder"].ToString();

            try
            {
                Console.WriteLine("*** Scanning all Team Projects in all Team Project Collections ***");

                string tfsRootUrl = ConfigurationManager.AppSettings["TfsRootUrl"].ToString();
                Console.WriteLine("Tfs Root Url: " + tfsRootUrl);

                Uri tfsConfigurationServerUri = new Uri(tfsRootUrl);
                TfsConfigurationServer configurationServer = TfsConfigurationServerFactory.GetConfigurationServer(tfsConfigurationServerUri);
                ITeamProjectCollectionService tpcService = configurationServer.GetService<ITeamProjectCollectionService>();

                string configDbConnectionString = ConfigurationManager.AppSettings["ConfigDBConnectionString"].ToString();
                Console.WriteLine("Config DB Connectionstring: " + configDbConnectionString);

                using (IVssDeploymentServiceHost deploymentServiceHost = CreateDeploymentServiceHost(configDbConnectionString))
                {
                    foreach (TeamProjectCollection tpc in tpcService.GetCollections().OrderBy(tpc => tpc.Name))
                    {
                        string nameOfLogFile = "UpgradeTeamProjectFeatures-" + tpc.Name + ".txt";
                        string fullPathOfLogFile = System.IO.Path.Combine(rootLogFolder, nameOfLogFile);
                        System.IO.StreamWriter logFile = new System.IO.StreamWriter(fullPathOfLogFile);

                        logFile.WriteLine(String.Format("*** scanning Team Projects in TPC {0} ***", tpc.Name));
                        logFile.WriteLine();

                        string tpcUrl = tfsRootUrl + "/" + tpc.Name;
                        TfsTeamProjectCollection tfsCollection = new TfsTeamProjectCollection(new Uri(tpcUrl));

                        WorkItemStore witStore = tfsCollection.GetService<WorkItemStore>();
                        foreach (Microsoft.TeamFoundation.WorkItemTracking.Client.Project project in witStore.Projects)
                        {
                            Console.WriteLine("Team Project " + project.Name);
                            logFile.WriteLine(">> Team Project " + project.Name);
                            RunFeatureEnablement(deploymentServiceHost, project, tfsCollection.InstanceId, logFile);
                            logFile.WriteLine();
                        }

                        logFile.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static string GetTfsInstallationDir(string tfsVersion = "15.0")
        {
            string registryKeyString = string.Format(@"SOFTWARE\Microsoft\TeamFoundationServer\{0}", tfsVersion);

            using (RegistryKey localMachineKey = Registry.LocalMachine.OpenSubKey(registryKeyString))
            {
                return localMachineKey.GetValue("InstallPath") as string;
            }
        }

        private static IVssDeploymentServiceHost CreateDeploymentServiceHost(string configDbConnectionString)
        {
            TeamFoundationServiceHostProperties deploymentHostProperties = new TeamFoundationServiceHostProperties();
            deploymentHostProperties.HostType = TeamFoundationHostType.Deployment | TeamFoundationHostType.Application;
            deploymentHostProperties.PlugInDirectory = Path.Combine(GetTfsInstallationDir(), @"Application Tier\TFSJobAgent\Plugins");
            return Microsoft.TeamFoundation.Framework.Server.DeploymentServiceHostFactory.CreateDeploymentServiceHost(deploymentHostProperties, SqlConnectionInfoFactory.Create(configDbConnectionString, null, null));
        }

        private static IVssRequestContext CreateServicingContext(IVssDeploymentServiceHost deploymentServiceHost, Guid instanceId)
        {
            using (IVssRequestContext requestContext = deploymentServiceHost.CreateSystemContext(true))
            {
                TeamFoundationHostManagementService host = requestContext.GetService<TeamFoundationHostManagementService>();
                return host.BeginRequest(requestContext, instanceId, RequestContextType.ServicingContext);
            }
        }

        private static void RunFeatureEnablement(IVssDeploymentServiceHost deploymentServiceHost, Microsoft.TeamFoundation.WorkItemTracking.Client.Project project, Guid instanceId, StreamWriter file)
        {
            try
            {
                Console.WriteLine("Running feature enablement for '{0}'", project.Name);

                using (IVssRequestContext context = CreateServicingContext(deploymentServiceHost, instanceId))
                {
                    ProvisionProjectFeatures(context, project, file);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Feature enablement failed for project '{0}': see log for details.", project.Name);
                file.WriteLine(">>> Feature enablement failed for project '{0}': {1}", project.Name, ex);
            }
        }

        private static void ProvisionProjectFeatures(IVssRequestContext context, Microsoft.TeamFoundation.WorkItemTracking.Client.Project project, StreamWriter logFile)
        {
            // Get the Feature provisioning service ("Configure Features")
            var projectFeatureProvisioningService = context.GetService<ProjectFeatureProvisioningService>();

            if (!projectFeatureProvisioningService.GetFeatures(context, project.Uri.ToString()).Where(f => (f.State == ProjectFeatureState.NotConfigured && !f.IsHidden)).Any())
            {
                // When the team project is already fully or partially configured, report it
                Console.WriteLine("\t{0}: Project is up to date.", project.Name);
                logFile.WriteLine(">>> Team Project is now already up to date");
            }
            else
            {
                // Find valid process templates
                var projectFeatureProvisioningDetails = projectFeatureProvisioningService.ValidateProcessTemplates(context, project.Uri.ToString());

                var validProcessTemplateDetails = projectFeatureProvisioningDetails.Where(d => d.IsValid);

                switch (validProcessTemplateDetails.Count())
                {
                    case 0:
                        Console.WriteLine("\t{0}: No valid process templates found.", project.Name);
                        logFile.WriteLine(">>> No valid process templates found, the team project cannot be configured/upgraded automatically to adopt the latest features.");
                        break;
                    case 1:
                        var projectFeatureProvisioningDetail = projectFeatureProvisioningDetails.ElementAt(0);
                        Console.WriteLine(">>> Upgrading Team Project with template " + projectFeatureProvisioningDetail.ProcessTemplateDescriptorName);
                        logFile.WriteLine(">>> Upgrading Team Project with template " + projectFeatureProvisioningDetail.ProcessTemplateDescriptorName);
                        ProvisionProject(context, project, projectFeatureProvisioningService, projectFeatureProvisioningDetail);
                        break;
                    default:
                        // Try to upgrade using the recommended process template
                        var newRecommendedTemplate = validProcessTemplateDetails.FirstOrDefault(ptd => ptd.IsRecommended);
                        Console.WriteLine(">>> Multiple valid process templates found. Upgrading Team Project with recommended template " + newRecommendedTemplate.ProcessTemplateDescriptorName);
                        logFile.WriteLine(">>> Multiple valid process templates found. Upgrading Team Project with recommended template " + newRecommendedTemplate.ProcessTemplateDescriptorName);
                        ProvisionProject(context, project, projectFeatureProvisioningService, newRecommendedTemplate);
                        break;
                }
            }
        }

        private static void ProvisionProject(IVssRequestContext context, Microsoft.TeamFoundation.WorkItemTracking.Client.Project project, ProjectFeatureProvisioningService projectFeatureProvisioningService, IProjectFeatureProvisioningDetails projectFeatureProvisioningDetail)
        {
            projectFeatureProvisioningService.ProvisionFeatures(context, project.Uri.ToString(), projectFeatureProvisioningDetail.ProcessTemplateDescriptorId);
        }
    }
}
