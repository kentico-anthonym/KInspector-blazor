﻿using System.Diagnostics;

using KInspector.Core.Models;
using KInspector.Core.Repositories.Interfaces;
using KInspector.Core.Services.Interfaces;

namespace KInspector.Infrastructure.Services
{
    public class VersionRepository : IVersionRepository
    {
        private readonly IDatabaseService databaseService;

        private static readonly string getCmsSettingsPath = @"Scripts/GetCmsSettings.sql";

        private const string _administrationDllToCheck = "CMS.DataEngine.dll";
        private const string _relativeAdministrationDllPath = "bin";
        private const string _relativeHotfixFileFolderPath = "App_Data\\Install";
        private const string _hotfixFile = "Hotfix.txt";

        public VersionRepository(IDatabaseService databaseService)
        {
            this.databaseService = databaseService;
        }

        public Version? GetKenticoAdministrationVersion(Instance instance)
        {
            return instance.AdministrationPath is null ? null : GetKenticoAdministrationVersion(instance.AdministrationPath);
        }

        public Version? GetKenticoAdministrationVersion(string rootPath)
        {
            if (!Directory.Exists(rootPath))
            {
                return null;
            }

            var binDirectory = Path.Combine(rootPath, _relativeAdministrationDllPath);
            if (!Directory.Exists(binDirectory))
            {
                return null;
            }

            var dllFileToCheck = Path.Combine(binDirectory, _administrationDllToCheck);
            if (!File.Exists(dllFileToCheck))
            {
                return null;
            }

            var fileVersionInfo = FileVersionInfo.GetVersionInfo(dllFileToCheck);
            var hotfix = "0";
            var hotfixDirectory = Path.Combine(rootPath, _relativeHotfixFileFolderPath);
            if (Directory.Exists(hotfixDirectory))
            {
                var hotfixFile = Path.Combine(hotfixDirectory, _hotfixFile);

                if (File.Exists(hotfixFile))
                {
                    hotfix = File.ReadAllText(hotfixFile);
                }
            }

            var version = $"{fileVersionInfo.FileMajorPart}.{fileVersionInfo.FileMinorPart}.{hotfix}";

            return new Version(version);
        }

        public Version? GetKenticoDatabaseVersion(DatabaseSettings databaseSettings)
        {
            databaseService.Configure(databaseSettings);
            var settingsKeys = databaseService.ExecuteSqlFromFile<string>(getCmsSettingsPath)
                .ToList();

            var version = settingsKeys[0];
            var hotfix = settingsKeys[1];

            if (version == null || hotfix == null) {
                return null;
            }

            return new Version($"{version}.{hotfix}");
        }
    }
}