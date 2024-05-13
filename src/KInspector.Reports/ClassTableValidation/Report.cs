﻿using KInspector.Core;
using KInspector.Core.Constants;
using KInspector.Core.Helpers;
using KInspector.Core.Models;
using KInspector.Core.Services.Interfaces;
using KInspector.Reports.ClassTableValidation.Models;

namespace KInspector.Reports.ClassTableValidation
{
    public class Report : AbstractReport<Terms>
    {
        private readonly IDatabaseService databaseService;
        private readonly IInstanceService instanceService;
        private readonly IConfigService configService;

        public Report(
            IDatabaseService databaseService,
            IInstanceService instanceService,
            IModuleMetadataService moduleMetadataService,
            IConfigService configService
            ) : base(moduleMetadataService)
        {
            this.databaseService = databaseService;
            this.instanceService = instanceService;
            this.configService = configService;
        }

        public override IList<Version> CompatibleVersions => VersionHelper.GetVersionList("10", "11", "12", "13");

        public override IList<string> Tags => new List<string> {
            ModuleTags.Health,
        };

        public override ModuleResults GetResults()
        {
            var instance = configService.GetCurrentInstance();
            var instanceDetails = instanceService.GetInstanceDetails(instance);
            var tablesWithMissingClass = GetResultsForTables(instanceDetails);
            var classesWithMissingTable = GetResultsForClasses();

            return CompileResults(tablesWithMissingClass, classesWithMissingTable);
        }

        private ModuleResults CompileResults(IEnumerable<TableWithNoClass> tablesWithMissingClass, IEnumerable<ClassWithNoTable> classesWithMissingTable)
        {
            var tableErrors = tablesWithMissingClass.Count();
            var tableResults = new TableResult()
            {
                Name = Metadata.Terms.DatabaseTablesWithMissingKenticoClasses,
                Rows = tablesWithMissingClass
            };

            var classErrors = classesWithMissingTable.Count();
            var classResults = new TableResult()
            {
                Name = Metadata.Terms.KenticoClassesWithMissingDatabaseTables,
                Rows = classesWithMissingTable
            };

            var totalErrors = tableErrors + classErrors;
            var results = new ModuleResults();
            switch (totalErrors)
            {
                case 0:
                    results.Status = ResultsStatus.Good;
                    results.Summary = Metadata.Terms.NoIssuesFound;
                    results.Type = ResultsType.NoResults;
                    break;

                default:
                    results.Status = ResultsStatus.Error;
                    results.Summary = Metadata.Terms.CountIssueFound?.With(new { count = totalErrors });
                    results.Type = ResultsType.TableList;
                    results.TableResults.Add(tableResults);
                    results.TableResults.Add(classResults);
                    break;
            }

            return results;
        }

        private IEnumerable<ClassWithNoTable> GetResultsForClasses()
        {
            var classesWithMissingTable = databaseService.ExecuteSqlFromFile<ClassWithNoTable>(Scripts.ClassesWithNoTable);
            return classesWithMissingTable;
        }

        private IEnumerable<TableWithNoClass> GetResultsForTables(InstanceDetails instanceDetails)
        {
            var tablesWithMissingClass = databaseService.ExecuteSqlFromFile<TableWithNoClass>(Scripts.TablesWithNoClass);

            var tableWhitelist = GetTableWhitelist(instanceDetails.AdministrationDatabaseVersion);
            if (tableWhitelist.Count > 0)
            {
                tablesWithMissingClass = tablesWithMissingClass.Where(t => !tableWhitelist.Contains(t.TableName ?? string.Empty)).ToList();
            }

            return tablesWithMissingClass;
        }

        private List<string> GetTableWhitelist(Version? version)
        {
            var whitelist = new List<string>();

            if (version?.Major >= 10)
            {
                whitelist.Add("CI_Migration");
            }

            return whitelist;
        }
    }
}