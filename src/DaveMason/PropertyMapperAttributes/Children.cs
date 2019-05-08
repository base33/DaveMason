﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;

namespace DaveMason.PropertyMapperAttributes
{
    public class Children : PropertyMapperAttribute
    {
        public override object ProcessValue()
        {
            if (Context.Property.PropertyType.GenericTypeArguments[0] == typeof (IPublishedContent))
            {
                return Content.Children;
            }

            IList list = (IList)Activator.CreateInstance(Context.Property.PropertyType);
            foreach (var child in Content.Children)
            {
                list.Add(child.As(Context.Property.PropertyType.GenericTypeArguments[0]));
            }


            return null;
        }
    }
}
