﻿using System;
using System.Linq;
using System.Reflection;
using Microsoft.OpenApi.Models;
using System.Collections.Generic;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ordercloud.integrations.library.helpers
{
	[AttributeUsage(AttributeTargets.Property)]
	public class DocIgnoreAttribute : Attribute
	{
	}

	public class SwaggerExcludeFilter : ISchemaFilter
	{
		public void Apply(OpenApiSchema model, SchemaFilterContext context)
		{
			var type = context.GetType();
			var excludeProperties = type.GetProperties().Where(t => t.GetCustomAttribute<DocIgnoreAttribute>() != null);
			if (excludeProperties != null)
			{
				foreach (var property in excludeProperties)
				{
					// Because swagger uses camel casing
					var propertyName = $@"{char.ToLower(property.Name[0])}{property.Name.Substring(1)}";
					if (model.Properties.ContainsKey(propertyName))
					{
						model.Properties.Remove(propertyName);
					}
				}
			}
		}
	}
}