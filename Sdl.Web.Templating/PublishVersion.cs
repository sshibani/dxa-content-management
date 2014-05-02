﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Helpers;
using System.Xml;
using System.Xml.Linq;
using Sdl.Web.Templating.ExtensionMethods;
using Tridion.ContentManager;
using Tridion.ContentManager.CommunicationManagement;
using Tridion.ContentManager.ContentManagement;
using Tridion.ContentManager.ContentManagement.Fields;
using Tridion.ContentManager.Templating;
using Tridion.ContentManager.Templating.Assembly;

namespace Sdl.Web.Templating
{
    /// <summary>
    /// Publish all files required to make a version release for the implementation:
    /// * Configuration
    /// * Resources
    /// * Schema, Template and Taxonomy ids (these are published only from the default localization/master web publication)
    /// * Design Elements (if applicable)
    /// * Views (if applicable)
    /// </summary>
    [TcmTemplateTitle("Publish Version")]
    [TcmTemplateParameterSchema("resource:Sdl.Web.Templating.Resources.Schemas.Publish Version Parameters.xsd")]
    public class PublishVersion : TemplateBase.TemplateBase
    {
        private const string BootstrapFilename = "_all";
        private const string TemplateConfigName = "templates";
        private const string SchemasConfigName = "schemas";
        private const string TaxonomiesConfigName = "taxonomies";
        private const string MappingsConfigName = "mappings";
        private const string VocabulariesConfigName = "vocabularies";
        private const string VocabulariesAppDataId = "http://www.sdl.com/tridion/SemanticMapping/vocabularies";
        private const string TypeOfAppDataId = "http://www.sdl.com/tridion/SemanticMapping/typeof";

        private string _moduleRoot = string.Empty;

        public override void Transform(Engine engine, Package package)
        {
            Initialize(engine, package);
            var folderWebDavUrl = GetPublication().RootFolder.WebDavUrl + "/" + package.GetValue("folderWebDavUrl");
            var sgUri = MPackage.GetValue("sg_TargetStructureGroup");
            var sg = (StructureGroup)MEngine.GetObject(engine.LocalizeUri(new TcmUri(sgUri)));
            Folder folder;
            try
            {
                folder = (Folder)MEngine.GetObject(folderWebDavUrl);
            }
            catch
            {
                throw new Exception(String.Format("Cannot find modules folder: {0}. Please check it exists and update your TBB parameters accordingly.",folderWebDavUrl));
            }
            _moduleRoot = folder.WebDavUrl;
            if (sg == null)
            {
                throw new Exception(String.Format("Could not find structure group {0} in context publication {1}", sgUri, GetPublication().Title));
            }
            List<string> bootstrapFiles = new List<string>
                {
                    Json.Encode(PublishAssets(folder, sg, "config")),
                    Json.Encode(PublishAssets(folder, sg, "mappings")),
                    Json.Encode(PublishAssets(folder, sg, "resources"))
                };
            Item jsonBootstrap = MPackage.CreateStringItem(ContentType.Text, String.Format("{{\"files\":[{0}]}}", String.Join(",", bootstrapFiles)));
            MPackage.PushItem(Package.OutputName, jsonBootstrap);
            MEngine.PublishingContext.RenderedItem.AddBinary(jsonBootstrap.GetAsStream(), BootstrapFilename + JsonExtension, sg, "bootstrap", GetComponent(), JsonMimetype);
        }

        private string PublishAssets(Folder rootFolder, StructureGroup rootSg, string type)
        {
            Folder folder = (Folder)GetChildOrganizationalItem(rootFolder, "_" + type);
            StructureGroup sg = (StructureGroup)GetChildOrganizationalItem(rootSg, type);
            if (folder == null)
            {
                throw new Exception(String.Format("Could not find Folder '_{0}' in module folder {1}", type, rootFolder.WebDavUrl));
            }
            if (sg == null)
            {
                throw new Exception(String.Format("Could not find Structure Group '{0}' in system Structure Group {1}", type, sg.WebDavUrl));
            }
            List<string> files = new List<string>();
            Component allComponent = null;
            bool isArray = false;
            foreach (var item in GetOrganizationalItemContents(folder, ItemType.Component, false))
            {
                var comp = (Component)MEngine.GetObject(item.Key.ToString());
                var configType = item.Value.ToLower();
                if (configType == BootstrapFilename)
                {
                    //There is a dummy _All component used to publish a bootstrap file containing a list of other files that have been generated
                    allComponent = comp;
                    continue;
                }
                var settings = new Dictionary<string, List<string>>();
                if (configType.Equals(TemplateConfigName) || configType.Equals(SchemasConfigName) || configType.Equals(TaxonomiesConfigName) || configType.Equals(MappingsConfigName))
                {
                    //template, schema and taxonomy config is only published from the master web publication/default localization
                    if (IsMasterWebPublication())
                    {
                        switch (configType)
                        {
                            case TemplateConfigName:
                                isArray = false;
                                settings = ReadTemplateData();
                                break;
                            case SchemasConfigName:
                                isArray = false;
                                settings = ReadSchemaData();
                                break;
                            case TaxonomiesConfigName:
                                isArray = false;
                                settings = ReadTaxonomiesData();
                                break;
                            case MappingsConfigName:
                                isArray = true;
                                settings = ReadMappingsData();
                                break;
                        }
                    }
                }
                else
                {
                    settings = new Dictionary<string,List<string>> {{configType, ReadComponentData(comp)}};
                }
                //Add all the JSON files to the package and publish them as binaries
                foreach (var key in settings.Keys)
                {
                    Item jsonItem;
                    if (isArray)
                    {
                        jsonItem = MPackage.CreateStringItem(ContentType.Text, String.Format("[{0}]", String.Join(",\n", settings[key]))); 
                    }
                    else
                    {
                        jsonItem = MPackage.CreateStringItem(ContentType.Text, String.Format("{{{0}}}", String.Join(",\n", settings[key])));
                    }
                    var filename = key + JsonExtension;
                    MPackage.PushItem(type + "/" + filename, jsonItem);
                    var binary = MEngine.PublishingContext.RenderedItem.AddBinary(jsonItem.GetAsStream(), filename, sg, key, comp, JsonMimetype);
                    files.Add(Json.Encode(binary.Url));
                }
            }
            if (allComponent == null)
            {
                throw new Exception(String.Format("Cannot find '_All' component. Please ensure that this component is in the {0} folder.", folder.WebDavUrl));
            }
            //We create a special asset file _all.js which contains the path of all other asset files of this type that have been published
            Item filesItem = MPackage.CreateStringItem(ContentType.Text, String.Format("{{\"defaultLocalization\":{0},\"files\":[{1}]}}", Json.Encode(IsMasterWebPublication()), String.Join(",", files)));
            MPackage.PushItem(type + "/" + BootstrapFilename + JsonExtension, filesItem);
            var bootstrapBinary = MEngine.PublishingContext.RenderedItem.AddBinary(filesItem.GetAsStream(), BootstrapFilename + JsonExtension, sg, type, allComponent, JsonMimetype);
            return bootstrapBinary.Url;
        }

        private static List<string> ReadComponentData(Component comp)
        {
            var fields = new ItemFields(comp.Content, comp.Schema);
            var settings = new List<string>();
            var configFields = fields.GetEmbeddedFields("settings");
            if (configFields.Any())
            {
                //either schema is a generic multival embedded name/value
                foreach (var setting in configFields)
                {
                    settings.Add(String.Format("{0}:{1}", Json.Encode(setting.GetTextValue("name")), Json.Encode(setting.GetTextValue("value"))));
                }
            }
            else
            {
                //... or its a custom schema with individual fields
                foreach (var field in fields)
                {
                    //TODO handle more types
                    if (field is TextField)
                    {
                        settings.Add(String.Format("{0}:{1}", Json.Encode(field.Name), Json.Encode(fields.GetTextValue(field.Name))));
                    }
                }
            }
            return settings;
        }

        private Dictionary<string, List<string>> ReadMappingsData()
        {
            // generate a list of vocabulary prefix and name from appdata
            var res = new Dictionary<string, List<string>> { { VocabulariesConfigName, new List<string>() } };
            ApplicationData globalAppData = MEngine.GetSession().SystemManager.LoadGlobalApplicationData(VocabulariesAppDataId);
            if (globalAppData != null)
            {
                XElement vocabulariesXml = XElement.Parse(Encoding.Unicode.GetString(globalAppData.Data));
                foreach (var vocabulary in vocabulariesXml.Elements())
                {
                    res[VocabulariesConfigName].Add(String.Format("{{{0}:{1}}}", Json.Encode(vocabulary.Attribute("prefix").Value), Json.Encode(vocabulary.Attribute("name").Value)));
                }
            }

            // generate a list of schema + id, separated by module
            var schemaFilter = new RepositoryItemsFilter(MEngine.GetSession())
            {
                Recursive = true,
                ItemTypes = new List<ItemType> { ItemType.Schema },
                BaseColumns = ListBaseColumns.Extended
            };
            foreach (XmlElement item in GetPublication().GetListItems(schemaFilter).ChildNodes)
            {
                var type = item.GetAttribute("Type");
                var subType = item.GetAttribute("SubType");
                // we only consider normal schemas (type=8 subtype=0)
                if ((type == "8" && subType == "0"))
                {
                    var id = item.GetAttribute("ID");
                    var schema = (Schema)MEngine.GetObject(id);
                    var module = GetModuleName(schema.WebDavUrl);
                    if (module != null)
                    {
                        var key = module + "." + SchemasConfigName;
                        if (!res.ContainsKey(key))
                        {
                            res.Add(key, new List<string>());
                        }

                        // add schema typeof from appdata
                        StringBuilder schemaSemantics = new StringBuilder();
                        ApplicationData appData = schema.LoadApplicationData(TypeOfAppDataId);
                        if (appData != null)
                        {
                            string typeOf = Encoding.Unicode.GetString(appData.Data);
                            schemaSemantics.Append(BuildSemanticsJson(typeOf, "vocab", "entity"));
                        }

                        // TODO: serialize schema fields xml to json in a smart way
                        // field: {"name":"something","isMultiValue":true,"semantics":[],"fields":[]}
                        // field semantics: {"vocab":"s","property":"headline"}
                        StringBuilder fields = new StringBuilder();

                        // load namespace manager with schema namespaces
                        XmlNamespaceManager nsmgr = new XmlNamespaceManager(schema.Xsd.OwnerDocument.NameTable);
                        nsmgr.AddNamespace("xsd", "http://www.w3.org/2001/XMLSchema");
                        nsmgr.AddNamespace("tcm", "http://www.tridion.com/ContentManager/5.0");
                        nsmgr.AddNamespace("mapping", "http://www.sdl.com/tridion/SemanticMapping");

                        // loop over all field elements in schema
                        bool first = true;
                        foreach (XmlNode fieldNode in schema.Xsd.SelectNodes(string.Format("/xsd:schema/xsd:element[@name='{0}']/xsd:complexType/xsd:sequence/xsd:element", schema.RootElementName), nsmgr))
                        {
                            if (first)
                            {
                                first = false;
                            }
                            else
                            {
                                fields.Append(",");
                            }
                            StringBuilder fieldSemantics = new StringBuilder();
                            StringBuilder embeddedFields = new StringBuilder();

                            // if maxOccurs is anything else than 1, it is a multi value field
                            bool isMultiValue = !fieldNode.Attributes["maxOccurs"].Value.Equals("1");

                            // read semantic mapping from field
                            // schema semantics: {"vocab":"s","entity":"Article"}
                            XmlNode propertyNode = fieldNode.SelectSingleNode("xsd:annotation/xsd:appinfo/tcm:ExtensionXml/mapping:property", nsmgr);
                            string property = propertyNode != null ? propertyNode.InnerText : null;
                            fieldSemantics.Append(BuildSemanticsJson(property, "vocab", "property"));

                            XmlNode typeOfNode = fieldNode.SelectSingleNode("xsd:annotation/xsd:appinfo/tcm:ExtensionXml/mapping:typeof", nsmgr);
                            string typeOf = typeOfNode != null ? typeOfNode.InnerText : null;
                            if (fieldSemantics.Length > 0 && !string.IsNullOrEmpty(typeOf))
                            {
                                fieldSemantics.Append(",");
                                fieldSemantics.Append(BuildSemanticsJson(typeOf, "vocab", "entity"));
                            }

                            // TODO: handle embedded fields

                            fields.AppendFormat("{{{0}:{1},{2}:{3},{4}:[{5}],{6}:[{7}]}}", 
                                Json.Encode("name"), Json.Encode(fieldNode.Attributes["name"].Value), 
                                Json.Encode("isMultiValue"), Json.Encode(isMultiValue), 
                                Json.Encode("semantics"), fieldSemantics, 
                                Json.Encode("fields"), embeddedFields);
                        }

                        res[key].Add(string.Format("{{{0}:{1},{2}:{3},{4}:[{5}],{6}:[{7}]}}", 
                            Json.Encode("id"), Json.Encode(schema.Id.ItemId),
                            Json.Encode("rootElement"), Json.Encode(schema.RootElementName),
                            Json.Encode("fields"), fields, Json.Encode("semantics"), schemaSemantics));
                    }
                }
            }
            return res;
        }

        // schema semantics: {"vocab":"s","entity":"Article"}
        // field semantics: {"vocab":"s","property":"headline"}
        // semantics: {"prefixName":"a","itemName":"b"},{"prefixName":"c","itemName":"d"}
        private static string BuildSemanticsJson(string input, string prefixName, string itemName)
        {
            StringBuilder semantics = new StringBuilder();
            if (!string.IsNullOrEmpty(input))
            {
                // input = "s:Article" but can also be "s:Article,x:Something"
                string[] values = input.Split(',');
                bool first = true;
                foreach (var value in values)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        semantics.Append(",");
                    }
                    string[] parts = value.Split(':');
                    semantics.AppendFormat("{{{0}:{1},{2}:{3}}}", Json.Encode(prefixName), Json.Encode(parts[0]), Json.Encode(itemName), Json.Encode(parts[1]));
                }
            }

            return semantics.ToString();
        }

        private Dictionary<string, List<string>> ReadTaxonomiesData()
        {
            //Generate a list of taxonomy + id
            var res = new Dictionary<string, List<string>>();
            var settings = new List<string>();
            var taxFilter = new TaxonomiesFilter(MEngine.GetSession()) { BaseColumns = ListBaseColumns.Extended };
            foreach (XmlElement item in GetPublication().GetListTaxonomies(taxFilter).ChildNodes)
            {
                var id = item.GetAttribute("ID");
                var taxonomy = (Category)MEngine.GetObject(id);
                settings.Add(String.Format("{0}:{1}", Json.Encode(GetKeyFromTaxonomy(taxonomy)), Json.Encode(taxonomy.Id.ItemId)));
            }
            res.Add("core." + TaxonomiesConfigName, settings);
            return res;
        }

        private Dictionary<string, List<string>> ReadSchemaData()
        {
            //Generate a list of schema + mapping details, separated by module
            var res = new Dictionary<string, List<string>>();
            var schemaFilter = new RepositoryItemsFilter(MEngine.GetSession())
                {
                    Recursive = true,
                    ItemTypes = new List<ItemType> {ItemType.Schema},
                    BaseColumns = ListBaseColumns.Extended
                };
            foreach (XmlElement item in GetPublication().GetListItems(schemaFilter).ChildNodes)
            {
                var type = item.GetAttribute("Type");
                var subType = item.GetAttribute("SubType");
                //We only consider normal schemas (type=8 subtype=0)
                if ((type == "8" && subType == "0"))
                {
                    var id = item.GetAttribute("ID");
                    var schema = (Schema)MEngine.GetObject(id);
                    var module = GetModuleName(schema.WebDavUrl);
                    if (module != null)
                    {
                        var key = module + "." + SchemasConfigName;
                        if (!res.ContainsKey(key))
                        {
                            res.Add(key, new List<string>());
                        }
                        res[key].Add(String.Format("{0}:{1}", Json.Encode(GetKeyFromSchema(schema)), Json.Encode(schema.Id.ItemId)));
                    }
                }
            }
            return res;
        }

        private Dictionary<string, List<string>> ReadTemplateData()
        {
            //Generate a list of dynamic CT + id, separated by module
            var res = new Dictionary<string, List<string>>();
            var templateFilter = new ComponentTemplatesFilter(MEngine.GetSession()) { BaseColumns = ListBaseColumns.Extended };
            foreach (XmlElement item in GetPublication().GetListComponentTemplates(templateFilter).ChildNodes)
            {
                var id = item.GetAttribute("ID");
                var template = (ComponentTemplate)MEngine.GetObject(id);
                //Only consider dynamic CTs
                if (template.IsRepositoryPublishable)
                {
                    var module = GetModuleName(template.WebDavUrl);
                    if (module != null)
                    {
                        var key = module + "." + TemplateConfigName;
                        if (!res.ContainsKey(key))
                        {
                            res.Add(key, new List<string>());
                        }
                        res[key].Add(String.Format("{0}:{1}", Json.Encode(GetKeyFromTemplate(template)), Json.Encode(template.Id.ItemId)));
                    }
                }
            }
            return res;
        }

        private static string GetKeyFromTaxonomy(Category taxonomy)
        {
            var key = taxonomy.XmlName;
            return key.Substring(0, 1).ToLower() + key.Substring(1);
        }

        private static string GetKeyFromTemplate(ComponentTemplate template)
        {
            var key = Regex.Replace(template.Title, @"[\[\]\s\.]", "");
            return key.Substring(0, 1).ToLower() + key.Substring(1);
        }

        private static string GetKeyFromSchema(Schema schema)
        {
            var key = schema.RootElementName;
            return key.Substring(0, 1).ToLower() + key.Substring(1);
        }

        private string GetModuleName(string fullItemWebdavUrl)
        {
            //The module name is the name of the folder within the first level of the module root folder 
            //in which the item lives
            if (fullItemWebdavUrl.StartsWith(_moduleRoot))
            {
                Logger.Debug(fullItemWebdavUrl + ":" + _moduleRoot);
                var res = fullItemWebdavUrl.Substring(_moduleRoot.Length + 1);
                var pos = res.IndexOf("/", StringComparison.Ordinal);
                Logger.Debug(res);
                return res.Substring(0, pos).ToLower();
            }
            return null;
        }
    }
}