﻿using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stacks.TagHelpers.Components;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Stacks.TagHelpers.Benchmarks
{
    public class TagHelperBenchmarks
    {
        private readonly RazorRenderer _renderer;

        public TagHelperBenchmarks()
        {
            _renderer = new RazorRenderer();
        }

        [Benchmark]
        public async Task With()
        {
            await _renderer.RenderAsync<object>("/Views/With.cshtml", null);
        }

        [Benchmark]
        public async Task Without()
        {
            await _renderer.RenderAsync<object>("/Views/Without.cshtml", null);
        }
    }

    class Program
    {
        static void Main()
        {

            BenchmarkRunner.Run<TagHelperBenchmarks>(new DebugInProcessConfig());
        }

    }

    public class RazorRenderer
    {
        public async Task<string> RenderAsync<TModel>(string name, TModel model)
        {
            // get this assembly so we can get the compiled razor pages out of it
            var assembly = RelatedAssemblyAttribute.GetRelatedAssemblies(typeof(TagHelperBenchmarks).Assembly, false).SingleOrDefault();

            // get all the compiled razor items and filter down to just the one we want
            var allItems = new RazorCompiledItemLoader().LoadItems(assembly);
            var razorCompiledItem = allItems
                .FirstOrDefault(item => item.Identifier == name);

            if (razorCompiledItem == null)
            {
                throw new Exception("Unable to find or compile item with name: " + name);
            }

            // execute the page and get the final string
            return await GetOutput(assembly, razorCompiledItem, model);
        }

        private static async Task<string> GetOutput<TModel>(Assembly assembly, RazorCompiledItem razorCompiledItem, TModel model)
        {
            using var output = new StringWriter();

            // create services and add all of MVC because we need the ITagHelperFactory and I'm too lazy to pick it out by itself
            var services = new ServiceCollection();
            services.AddMvc();
            // add in the SvgTagHelperConfiguration so the views don't crash at runtime
            services.AddSvgTagHelper(new SvgTagHelperConfiguration
            {
                SvgFolderPath = "" //TODO
            });
            var serviceProvider = services.BuildServiceProvider();

            // get the compiled RazorPage from the assembly
            var compiledTemplate = assembly.GetType(razorCompiledItem.Type.FullName);
            var razorPage = (RazorPage)Activator.CreateInstance(compiledTemplate);

            // set the ViewData to our model
            if (razorPage is RazorPage<TModel> page)
            {
                page.ViewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = model
                };

            }

            // set the ViewContext + HttpContext w/ services
            razorPage.ViewContext = new ViewContext
            {
                HttpContext = new DefaultHttpContext {
                    ServiceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>()
                },
                Writer = output
            };

            // necessary fluff
            razorPage.DiagnosticSource = new DiagnosticListener("GetOutput");
            razorPage.HtmlEncoder = HtmlEncoder.Default;

            // actually execute the razor page
            await razorPage.ExecuteAsync();

            // return the compiled page as a string
            return output.ToString();
        }
    }
}
