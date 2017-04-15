# TFSUpgradeTeamProjectFeatures
Tool to upgrade existing/old Team Projects to latest features

Used for upgrading Team Projects after a migration to TFS 2017 Update 1. Instead of running the Configure Features wizard one by one for many different Team Projects spread across multiple Team Project Collections, this tool allows to scan all available Team Projects and to apply new features of the recommended Team Project process template.

The reposistory contains one solution with a console application project (created with Visual Studio 2017).

The config file contains three mandatory configuration settings:
* TfsRootUrl [http://servername:8080/tfs]
* RootLogFolder [D:\Logs\]
* ConfigDBConnectionString [Data Source=DBServerName;Initial Catalog=Tfs_Configuration;Integrated Security=True]

## What will it do?
* When no valid process template is found for the Team Project to upgrade, it means the wizard cannot upgrade the Team Project automatically. The upgrade must be done manually.
* When the Team Project already adopts the new features, nothing will be done
* When the process only finds 1 appropriate process template for upgrade, it will perform the upgrade
* When the process finds multiple valid process templates for upgrade, it will run the upgrade based on the recommended process template

## How to run?

Copy the .exe and exe.config file to the bin folder of the TFS Application Tier server (C:\Program Files\Microsoft Team Foundation Server 11.0\Application Tier\Web Services\biin) and run it from the command line. Be sure to use this tool during your trial-upgrade before applying this into a production environment.

## LogFiles?

The tool will write some log messages to the console, but it also creates a dedicated log file for every Team Project Collection with an overview of the performed actions on the various Team Projects. When the process performs the upgrade, many run-time errors may still block the upgrade and you might need to perform the upgrade yourself.

## Credits

* https://www.visualstudio.com/en-us/docs/work/customize/configure-features-after-upgrade#program-updates

* Features4tfs: https://features4tfs.codeplex.com/SourceControl/latest#features4tfs.2015/Program.cs

