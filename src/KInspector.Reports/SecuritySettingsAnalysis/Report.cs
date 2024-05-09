﻿using System.Xml;
using System.Xml.Linq;

using KInspector.Core;
using KInspector.Core.Constants;
using KInspector.Core.Helpers;
using KInspector.Core.Models;
using KInspector.Core.Services.Interfaces;
using KInspector.Reports.SecuritySettingsAnalysis.Analyzers;
using KInspector.Reports.SecuritySettingsAnalysis.Models;
using KInspector.Reports.SecuritySettingsAnalysis.Models.Data;
using KInspector.Reports.SecuritySettingsAnalysis.Models.Results;

namespace KInspector.Reports.SecuritySettingsAnalysis
{
    public class Report : AbstractReport<Terms>
    {
        private readonly IDatabaseService databaseService;
        private readonly IInstanceService instanceService;
        private readonly ICmsFileService cmsFileService;
        private readonly IConfigService configService;

        public override IList<Version> CompatibleVersions => VersionHelper.GetVersionList("10", "11", "12", "13");

        public override IList<string> Tags => new List<string>
        {
            ReportTags.Security,
            ReportTags.Configuration
        };

        public Report(
            IDatabaseService databaseService,
            IInstanceService instanceService,
            ICmsFileService cmsFileService,
            IModuleMetadataService moduleMetadataService,
            IConfigService configService
            ) : base(moduleMetadataService)
        {
            this.databaseService = databaseService;
            this.instanceService = instanceService;
            this.cmsFileService = cmsFileService;
            this.configService = configService;
        }

        public override ReportResults GetResults()
        {
            var cmsSettingsKeysNames = new SettingsKeyAnalyzers(null)
                .Analyzers
                .Select(analyzer => analyzer.Parameters[0].Name);

            var cmsSettingsKeys = databaseService.ExecuteSqlFromFile<CmsSettingsKey>(
                Scripts.GetSecurityCmsSettings,
                new { cmsSettingsKeysNames }
                );

            var cmsSettingsKeyResults = GetCmsSettingsKeyResults(cmsSettingsKeys);

            var cmsSettingsCategoryIdsOnPaths = cmsSettingsKeyResults
                .SelectMany(cmsSettingsKeyResult => cmsSettingsKeyResult.GetCategoryIdsOnPath())
                .Distinct();

            var cmsSettingsCategories = databaseService.ExecuteSqlFromFile<CmsSettingsCategory>(
                Scripts.GetCmsSettingsCategories,
                new { cmsSettingsCategoryIdsOnPaths }
                );

            var currentInstance = configService.GetCurrentInstance();
            var sites = instanceService
                .GetInstanceDetails(currentInstance)?
                .Sites?
                .Append(new Site()
                {
                    Id = 0,
                    Name = Metadata.Terms.GlobalSiteName
                });

            var instancePath = currentInstance?.AdministrationPath;
            var resxValues = cmsFileService.GetResourceStringsFromResx(instancePath);
            var localizedCmsSettingsKeyResults = cmsSettingsKeyResults
                .Select(cmsSettingsKeyResult => new CmsSettingsKeyResult(
                    cmsSettingsKeyResult,
                    cmsSettingsCategories,
                    sites ?? Enumerable.Empty<Site>(),
                    resxValues
                    ));

            var webConfigXml = cmsFileService.GetXmlDocument(instancePath, DefaultKenticoPaths.WebConfigFile);
            var webConfigSettingsResults = GetWebConfigSettingsResults(webConfigXml);

            return CompileResults(localizedCmsSettingsKeyResults, webConfigSettingsResults);
        }

        private IEnumerable<CmsSettingsKeyResult> GetCmsSettingsKeyResults(IEnumerable<CmsSettingsKey> cmsSettingsKeys)
        {
            var analyzersObject = new SettingsKeyAnalyzers(Metadata.Terms);

            foreach (var analyzer in analyzersObject.Analyzers)
            {
                var analysisResult = analyzersObject.GetAnalysis(analyzer, cmsSettingsKeys, key => key.KeyName);

                if (analysisResult is CmsSettingsKeyResult cmsSettingsKeyResult)
                {
                    yield return cmsSettingsKeyResult;
                }
            }
        }

        private IEnumerable<WebConfigSettingResult> GetWebConfigSettingsResults(XmlDocument? webConfigXml)
        {
            if (webConfigXml is null)
            {
                return Enumerable.Empty<WebConfigSettingResult>();
            }

            // This conversion is temporary pending standardization of XDocument usage
            var webConfig = ToXDocument(webConfigXml);

            var appSettings = webConfig
                .Descendants("appSettings")
                .Elements();

            var appSettingsResults = GetAppSettingsResults(appSettings);

            var systemWebElements = webConfig
                .Descendants("system.web")
                .Elements();

            var systemWebSettingsResults = GetSystemWebSettingsResults(systemWebElements);

            var connectionStrings = webConfig
                .Descendants("connectionStrings")
                .Elements();

            var connectionStringElementsResults = GetConnectionStringsResults(connectionStrings);

            var webConfigSettingsResults = appSettingsResults
                .Concat(systemWebSettingsResults)
                .Concat(connectionStringElementsResults);

            return webConfigSettingsResults;
        }

        private IEnumerable<WebConfigSettingResult> GetAppSettingsResults(IEnumerable<XElement> appSettings)
        {
            var analyzersObject = new AppSettingAnalyzers(Metadata.Terms);

            foreach (var analyzer in analyzersObject.Analyzers)
            {
                var analysisResult = analyzersObject.GetAnalysis(analyzer, appSettings, key => key.Attribute("key")?.Value);

                if (analysisResult is WebConfigSettingResult appSettingResult)
                {
                    yield return appSettingResult;
                }
            }
        }

        private IEnumerable<WebConfigSettingResult> GetSystemWebSettingsResults(IEnumerable<XElement> systemWebElements)
        {
            var analyzersObject = new SystemWebSettingAnalyzers(Metadata.Terms);

            foreach (var analyzer in analyzersObject.Analyzers)
            {
                var analysisResult = analyzersObject.GetAnalysis(analyzer, systemWebElements, key => key.Name.LocalName);

                if (analysisResult is WebConfigSettingResult systemWebElementsResult)
                {
                    yield return systemWebElementsResult;
                }
            }
        }

        private IEnumerable<WebConfigSettingResult> GetConnectionStringsResults(IEnumerable<XElement> connectionStringElements)
        {
            var analyzersObject = new ConnectionStringAnalyzers(Metadata.Terms);

            foreach (var analyzer in analyzersObject.Analyzers)
            {
                var analysisResult = analyzersObject.GetAnalysis(
                    analyzer,
                    connectionStringElements,
                    key => key.Attribute("name")?.Value
                    );

                if (analysisResult is WebConfigSettingResult connectionStringResult)
                {
                    yield return connectionStringResult;
                }
            }
        }

        private ReportResults CompileResults(
            IEnumerable<CmsSettingsKeyResult> cmsSettingsKeyResults,
            IEnumerable<WebConfigSettingResult> webConfigSettingsResults
            )
        {
            if (!cmsSettingsKeyResults.Any() && !webConfigSettingsResults.Any())
            {
                return new ReportResults
                {
                    Status = ResultsStatus.Good,
                    Summary = Metadata.Terms.Summaries?.Good,
                    Type = ResultsType.NoResults
                };
            }

            var errorReportResults = new ReportResults
            {
                Type = ResultsType.TableList,
                Status = ResultsStatus.Warning
            };

            var cmsSettingsKeyResultsCount = IfAnyAddTableResult(
                errorReportResults.Data,
                cmsSettingsKeyResults,
                Metadata.Terms.TableTitles?.AdminSecuritySettings
                );

            var webConfigSettingsResultsCount = IfAnyAddTableResult(
                errorReportResults.Data,
                webConfigSettingsResults,
                Metadata.Terms.TableTitles?.WebConfigSecuritySettings
                );

            errorReportResults.Summary = Metadata.Terms.Summaries?.Warning?.With(new
            {
                cmsSettingsKeyResultsCount,
                webConfigSettingsResultsCount
            });

            return errorReportResults;
        }

        private static int IfAnyAddTableResult<T>(dynamic data, IEnumerable<T> results, Term tableNameTerm)
        {
            if (results.Any())
            {
                var tableResult = new TableResult<T>
                {
                    Name = tableNameTerm,
                    Rows = results
                };

                IDictionary<string, object> dictionaryData = data;

                dictionaryData.Add(tableNameTerm, tableResult);
            }

            return results.Count();
        }

        private static XDocument ToXDocument(XmlDocument document, LoadOptions options = LoadOptions.None)
        {
            using (var reader = new XmlNodeReader(document))
            {
                return XDocument.Load(reader, options);
            }
        }
    }
}