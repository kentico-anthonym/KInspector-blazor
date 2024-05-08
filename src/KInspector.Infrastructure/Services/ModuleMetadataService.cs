using KInspector.Core.Models;
using KInspector.Core.Services.Interfaces;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KInspector.Core.Helpers
{
    public class ModuleMetadataService : IModuleMetadataService
    {
        private const string DEFAULT_CULTURE_NAME = "en-US";

        private readonly IConfigService configService;
        private readonly IInstanceService instanceService;

        public string DefaultCultureName => DEFAULT_CULTURE_NAME;

        public string CurrentCultureName => Thread.CurrentThread.CurrentCulture.Name;

        public ModuleMetadataService(IConfigService configService, IInstanceService instanceService)
        {
            this.configService = configService;
            this.instanceService = instanceService;
        }

        public ModuleMetadata<T> GetModuleMetadata<T>(string moduleCodename)
            where T : new()
        {
            var metadataDirectory = $"{DirectoryHelper.GetExecutingDirectory()}\\{moduleCodename}\\Metadata\\";
            var currentMetadata = DeserializeMetadataFromYamlFile<ModuleMetadata<T>>(
                metadataDirectory,
                CurrentCultureName,
                false
            );

            var currentCultureIsDefaultCulture = CurrentCultureName == DEFAULT_CULTURE_NAME;
            var mergedMetadata = new ModuleMetadata<T>();
            if (!currentCultureIsDefaultCulture)
            {
                var defaultMetadata = DeserializeMetadataFromYamlFile<ModuleMetadata<T>>(
                    metadataDirectory,
                    DefaultCultureName,
                    true
                );

                mergedMetadata = GetMergedMetadata(defaultMetadata, currentMetadata);
            }

            var moduleMetadata = currentCultureIsDefaultCulture ? currentMetadata : mergedMetadata;
            var currentInstance = configService.GetCurrentInstance();
            var instanceDetails = instanceService.GetInstanceDetails(currentInstance);
            var commonData = new
            {
                instanceUrl = currentInstance?.AdministrationUrl,
                administrationVersion = instanceDetails.AdministrationVersion,
                databaseVersion = instanceDetails.AdministrationDatabaseVersion
            };

            Term? name = moduleMetadata.Details.Name;
            moduleMetadata.Details.Name = name.With(commonData);

            Term? shortDescription = moduleMetadata.Details.ShortDescription;
            moduleMetadata.Details.ShortDescription = shortDescription.With(commonData);

            Term? longDescription = moduleMetadata.Details.LongDescription;
            moduleMetadata.Details.LongDescription = longDescription.With(commonData);

            return moduleMetadata;
        }

        private static T DeserializeMetadataFromYamlFile<T>(
            string metadataDirectory,
            string cultureName,
            bool ignoreUnmatchedProperties)
            where T : new()
        {
            var ModuleMetadataPath = new List<string> { cultureName, DEFAULT_CULTURE_NAME }
                .Select(culture => $"{metadataDirectory}{culture}.yaml")
                .FirstOrDefault(path => File.Exists(path));

            if (!String.IsNullOrEmpty(ModuleMetadataPath))
            {
                var fileText = File.ReadAllText(ModuleMetadataPath);

                return DeserializeYaml<T>(fileText, ignoreUnmatchedProperties);
            }

            return new T();
        }

        private static T DeserializeYaml<T>(
            string yaml,
            bool ignoreUnmatchedProperties)
        {
            var deserializerBuilder = new DeserializerBuilder()
                .WithNamingConvention(new CamelCaseNamingConvention());

            if (ignoreUnmatchedProperties)
            {
                deserializerBuilder.IgnoreUnmatchedProperties();
            }

            var deserializer = deserializerBuilder.Build();

            return deserializer.Deserialize<T>(yaml);
        }

        private static ModuleMetadata<T> GetMergedMetadata<T>(
            ModuleMetadata<T> defaultMetadata,
            ModuleMetadata<T> overrideMetadata)
            where T : new()
        {
            var mergedMetadata = new ModuleMetadata<T>();

            mergedMetadata.Details.Name = overrideMetadata.Details.Name ?? defaultMetadata.Details.Name;
            mergedMetadata.Details.ShortDescription = overrideMetadata.Details.ShortDescription ?? defaultMetadata.Details.ShortDescription;
            mergedMetadata.Details.LongDescription = overrideMetadata.Details.LongDescription ?? defaultMetadata.Details.LongDescription;

            RecursivelySetPropertyValues(
                typeof(T),
                defaultMetadata.Terms,
                overrideMetadata.Terms,
                mergedMetadata.Terms);

            return mergedMetadata;
        }

        private static void RecursivelySetPropertyValues(
            Type objectType,
            object defaultObject,
            object overrideObject,
            object targetObject)
        {
            var objectTypeProperties = objectType.GetProperties();

            foreach (var objectTypeProperty in objectTypeProperties)
            {
                var objectTypePropertyType = objectTypeProperty.PropertyType;

                var defaultObjectPropertyValue = objectTypeProperty.GetValue(defaultObject);

                object overrideObjectPropertyValue = overrideObject != null
                    ? objectTypeProperty.GetValue(overrideObject) 
                    : defaultObjectPropertyValue;

                if (objectTypePropertyType.Namespace == objectType.Namespace)
                {
                    var targetObjectPropertyValue = Activator.CreateInstance(objectTypePropertyType);

                    objectTypeProperty.SetValue(targetObject, targetObjectPropertyValue);

                    RecursivelySetPropertyValues(
                        objectTypePropertyType,
                        defaultObjectPropertyValue,
                        overrideObjectPropertyValue,
                        targetObjectPropertyValue);
                }
                else
                {
                    objectTypeProperty.SetValue(targetObject, overrideObjectPropertyValue);
                }
            }
        }
    }
}
