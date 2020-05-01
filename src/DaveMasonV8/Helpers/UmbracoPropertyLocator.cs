﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;

namespace DaveMason.Helpers
{
    internal class UmbracoPropertyLocator
    {
        public IPublishedProperty GetClosestProperty(IPublishedContent content, string propertyName = "")
        {
            return content.Properties.FirstOrDefault(property => property.Alias.ToLower() == propertyName.ToLower());
        }
    }
}