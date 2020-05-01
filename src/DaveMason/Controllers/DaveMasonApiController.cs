﻿using DaveMason.CodeGenerator;
using DaveMason.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Web.WebApi;

namespace DaveMason.Controllers
{
    public class DaveMasonApiController : UmbracoAuthorizedApiController
    {
        private enum ClassType
        {
            CLASS,
            INTERFACE
        }

        [HttpGet]
        public IEnumerable<IContentType> GetDocumentTypes()
        {
            var documentTypes = ApplicationContext.Services.ContentTypeService.GetAllContentTypes();
            return documentTypes.Where(documentType => documentType.PropertyGroups.Any()).OrderBy(x => x.Name);
        }

        public string GetDocumentTypeCode(int documentTypeId)
        {
            var documentType = ApplicationContext.Services.ContentTypeService.GetAllContentTypes(documentTypeId).FirstOrDefault();
            var typeModels = InitTypeModels(documentType);
            var printedClass = PrintModelToHtml(typeModels);
            return printedClass;
        }

        private DaveMasonGeneratedClass InitTypeModels(IContentType documentType)
        {
            var properties = documentType.PropertyGroups;
            var inheritedProperties = documentType.CompositionPropertyGroups.Where(x => !properties.Contains(x));

            var className = GetSafeName(documentType.Name);

            var parent = GetParentModel(documentType);
            var root = GenerateClass(documentType, properties, className);
            root.ParentClass = parent.ParentClass;

            var inherited = string.Empty;
            foreach (var propertyGroup in inheritedProperties)
            {
                className = GetSafeName(propertyGroup.Name);
                var inheritedClass = GenerateClass(documentType, propertyGroup.AsEnumerableOfOne(), className);

                var exists = root.Compositions.FirstOrDefault(x => x.ClassName == inheritedClass.ClassName);

                if (exists != null)
                {
                    exists.PropertyTypes.AddRange(inheritedClass.PropertyTypes);
                }
                else
                {
                    root.Compositions.Add(inheritedClass);
                }
            }

            return root;
        }

        private string PrintModelToHtml(DaveMasonGeneratedClass model)
        {
            var builder = new StringBuilder();

            if (model.ParentClass != null)
            {
                var parentClass = PrintModelToHtml(model.ParentClass);
                builder.Append(parentClass);
            }
            else
            {
                foreach (var composition in model.Compositions)
                {
                    var compositionInterface = PrintClassInterfaceHtml(composition, ClassType.INTERFACE);
                    builder.Append(compositionInterface);
                }
            }

            var classHtml = PrintClassInterfaceHtml(model, ClassType.CLASS);
            builder.Append(classHtml);


            var result = builder.ToString();
            return result;
        }

        private DaveMasonGeneratedClass GetParentModel(IContentType documentType)
        {
            var className = GetSafeName(documentType.Name);
            var properties = documentType.PropertyGroups;
            var root = GenerateClass(documentType, properties, className);


            if (documentType.ParentId > -1)
            {
                var parent = ApplicationContext.Services.ContentTypeService.GetAllContentTypes(documentType.ParentId).FirstOrDefault();
                if (parent != null)
                {
                    root.ParentClass = GetParentModel(parent);
                }
            }

            var inheritedProperties = documentType.CompositionPropertyGroups.Where(x => !properties.Contains(x));
            var inherited = string.Empty;
            foreach (var propertyGroup in inheritedProperties)
            {
                className = GetSafeName(propertyGroup.Name);
                var inheritedClass = GenerateClass(documentType, propertyGroup.AsEnumerableOfOne(), className);

                var exists = root.Compositions.FirstOrDefault(x => x.ClassName == inheritedClass.ClassName);

                if (exists != null)
                {
                    exists.PropertyTypes.AddRange(inheritedClass.PropertyTypes);
                }
                else
                {
                    root.Compositions.Add(inheritedClass);
                }
            }


            return root;
        }

        private DaveMasonGeneratedClass GenerateClass(IContentType documentType, IEnumerable<PropertyGroup> properties, string className)
        {
            var root = new DaveMasonGeneratedClass();
            root.ClassName = className;

            var publishedContentType = PublishedContentType.Get(PublishedItemType.Content, documentType.Alias);

            foreach (var propertyGroup in properties)
            {
                foreach (var propertyType in propertyGroup.PropertyTypes.OrderBy(pt => pt.Name))
                {
                    var publishedPropertyType = publishedContentType.GetPropertyType(propertyType.Alias);

                    if (publishedPropertyType == null)
                    {
                        throw new Exception($"Chill: could not get published property type {documentType.Alias}.{propertyType.Alias}.");
                    }

                    var propertyModelClrType = publishedPropertyType.ClrType;

                    var _propertyType = ResolvePropertyType(publishedPropertyType);
                    var _propertyName = char.ToUpper(propertyType.Alias[0]) + propertyType.Alias.Substring(1);


                    if (_propertyType.Contains("IPublishedContent"))
                    {
                        var preValues = ApplicationContext.Services.DataTypeService.GetPreValuesCollectionByDataTypeId(propertyType.DataTypeDefinitionId);
                        var filters = preValues.PreValuesAsDictionary.FirstOrDefault(x => x.Key == "filter");

                        if (filters.Value != null)
                        {
                            var preValue = filters.Value.Value;
                            if (!string.IsNullOrEmpty(preValue) && !preValue.Contains(','))
                            {
                                var mappedClass = char.ToUpper(preValue[0]) + preValue.Substring(1);
                                _propertyType = _propertyType.Replace("IPublishedContent", mappedClass);
                            }
                        }
                        else
                        {
                            var contentTypes = preValues.PreValuesAsDictionary.FirstOrDefault(x => x.Key == "contentTypes");
                            if (contentTypes.Value != null)
                            {
                                var preValue = contentTypes.Value.Value;
                                if (!string.IsNullOrEmpty(preValue))
                                {
                                    preValue = preValue.Trim('[').Trim(']');
                                    var nestedContent = JsonConvert.DeserializeObject<DaveMasonNestedContent>(preValue);
                                    if (!string.IsNullOrEmpty(nestedContent.ncAlias) && !nestedContent.ncAlias.Contains(','))
                                    {
                                        var mappedClass = char.ToUpper(nestedContent.ncAlias[0]) + nestedContent.ncAlias.Substring(1);
                                        _propertyType = _propertyType.Replace("IPublishedContent", mappedClass);
                                    }
                                }
                            }
                        }
                    }

                    if (_propertyName == className)
                    {
                        _propertyName = "_" + _propertyName;
                    }

                    root.PropertyTypes.Add(new DaveMasonPropertyType
                    {
                        PropertyGroup = GetSafeName(propertyGroup.Name),
                        Name = _propertyName,
                        PropertyType = _propertyType,
                        IsMandatory = propertyType.Mandatory
                    });
                }
            }

            return root;
        }

        private string PrintClassInterfaceHtml(DaveMasonGeneratedClass typeModels, ClassType classType)
        {
            var builder = new StringBuilder();

            builder.Append(HtmlPrinter.Keyword("public"));
            builder.Append(HtmlPrinter.Keyword(classType.ToString().ToLower()));

            if (classType == ClassType.CLASS)
            {
                builder.Append(HtmlPrinter.Type(typeModels.ClassName));

                if (typeModels.ParentClass == null)
                {
                    if (typeModels.InheritedInterfaceNames != null && typeModels.InheritedInterfaceNames.Any())
                    {
                        builder.Append(HtmlPrinter.Standard(" : "));
                        builder.Append(HtmlPrinter.Type(string.Join(", ", typeModels.InheritedInterfaceNames)));
                    }
                }
                else
                {
                    builder.Append(HtmlPrinter.Standard(" : "));
                    builder.Append(HtmlPrinter.Type(typeModels.ParentClass.ClassName));
                }
            }
            else
            {
                builder.Append(HtmlPrinter.Type(typeModels.InterfaceName));
            }

            builder.Append(HtmlPrinter.NewLine);
            builder.Append(HtmlPrinter.Standard("{"));
            builder.Append(HtmlPrinter.NewLine);

            var propertyTypes = typeModels.PropertyTypes;

            if (classType == ClassType.CLASS && typeModels.ParentClass == null)
            {
                List<DaveMasonPropertyType> list = new List<DaveMasonPropertyType>();
                foreach (var i in typeModels.Compositions)
                {
                    list.AddRange(i.PropertyTypes);
                }
                propertyTypes.AddRange(list);
            }


            foreach (var prop in propertyTypes.OrderByDescending(pt => pt.IsMandatory))
            {
                var mandatory = classType == ClassType.CLASS && prop.IsMandatory;
                if (mandatory)
                {
                    builder.Append(HtmlPrinter.Standard("[", false));
                    builder.Append(HtmlPrinter.Type("Required", false));
                    builder.Append(HtmlPrinter.Standard("]"));
                    builder.Append(HtmlPrinter.NewLine);
                }

                if (classType == ClassType.CLASS)
                {
                    builder.Append(HtmlPrinter.Keyword("public"));
                }

                var htmlClass = "dm-typ";

                var propertyTypeName = prop.PropertyType.
                    ReplaceFirstOccurrence("<", "<span class=\"dm-std\">&lt;</span>").
                    ReplaceLastOccurrence(">", "<span class=\"dm-std\">&gt;</span>");

                if (AliasTypesMap.Contains(propertyTypeName))
                {
                    htmlClass = "dm-kwd";
                }

                builder.AppendFormat("<span class=\"{0}\">{1}</span>", htmlClass, propertyTypeName);


                builder.Append(HtmlPrinter.Space);
                builder.Append(HtmlPrinter.Standard(prop.Name));
                builder.Append(HtmlPrinter.Standard("{"));
                builder.Append(HtmlPrinter.Keyword("get"));
                builder.Append(HtmlPrinter.Standard(";"));
                builder.Append(HtmlPrinter.Space);
                builder.Append(HtmlPrinter.Keyword("set"));
                builder.Append(HtmlPrinter.Standard(";"));
                builder.Append(HtmlPrinter.Space);
                builder.Append(HtmlPrinter.Standard("}"));
                builder.Append(HtmlPrinter.NewLine);

                if (mandatory)
                {
                    builder.Append(HtmlPrinter.NewLine);
                }

            }

            builder.Append(HtmlPrinter.Standard("}"));
            builder.Append(HtmlPrinter.NewLine);
            builder.Append(HtmlPrinter.NewLine);

            var result = builder.ToString();
            return result;
        }

        private string ResolvePropertyType(PublishedPropertyType propType)
        {
            var propertyType = string.Empty;

            var interfaces = propType.ClrType.GetInterfaces();

            if (interfaces.Any() && interfaces.Count() == 1)
            {
                propertyType = interfaces.First().Name;
                var args = propType.ClrType.GenericTypeArguments;
                if (args.Any())
                {
                    propertyType += "<";

                    foreach (var arg in args)
                    {
                        var mappedType = arg.Name;
                        if (TypesMap.ContainsKey(arg.Name))
                        {
                            mappedType = TypesMap[arg.Name];
                        }
                        propertyType += $"{mappedType},";
                    }
                    propertyType = propertyType.Trim(',');
                    propertyType += ">";
                }

            }
            else
            {
                var mappedType = string.Empty;

                if (TypesMap.ContainsKey(propType.ClrType.FullName))
                {
                    mappedType = TypesMap[propType.ClrType.FullName];
                }


                if (string.IsNullOrEmpty(mappedType))
                {
                    mappedType = propType.ClrType.Name;
                }

                propertyType = mappedType;

            }

            return propertyType;
        }

        private string GetSafeName(string name)
        {
            return name.Replace(" ", string.Empty).Replace("&", string.Empty);
        }

        private static readonly string[] AliasTypesMap = new string[]
        {
            "string",
            "int" ,
            "long" ,
            "object" ,
            "bool" ,
            "char",
            "byte" ,
            "float" ,
            "double",
            "decimal"
        };

        private static readonly IDictionary<string, string> TypesMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Newtonsoft.Json.Linq.JToken", "string" },
            { "System.Web.IHtmlString", "string" },
            { "String", "string" },
            { "System.Int16", "short" },
            { "System.Int32", "int" },
            { "System.Int64", "long" },
            { "System.String", "string" },
            { "System.Object", "object" },
            { "System.Boolean", "bool" },
            { "System.Void", "void" },
            { "System.Char", "char" },
            { "System.Byte", "byte" },
            { "System.UInt16", "ushort" },
            { "System.UInt32", "uint" },
            { "System.UInt64", "ulong" },
            { "System.SByte", "sbyte" },
            { "System.Single", "float" },
            { "System.Double", "double" },
            { "System.Decimal", "decimal" }
        };
    }

}