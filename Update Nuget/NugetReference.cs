﻿using Olive;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace MSharp.Build.UpdateNuget
{
    public class NugetReference
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Latest { get; set; }
        public bool ShouldUpdate;
        static Dictionary<string, string> NugetVersions = new Dictionary<string, string>();
        static DateTime NugetVersionsValidUntil = DateTime.Now.AddDays(-1);

        public NugetReference(string package, string version, MicroserviceItem service, SolutionProject project)
        {
            Name = package;
            Version = version;
            Latest = FindLatest();
            Project = project;
            Service = service;
        }

        public SolutionProject Project { get; }
        public MicroserviceItem Service { get; }

        public bool IsUpToDate => Version == Latest;

        public string FindLatest()
        {
            if (NugetVersionsValidUntil < DateTime.Now)
            {
                NugetVersions = new Dictionary<string, string>();
                NugetVersionsValidUntil = DateTime.Now.AddMinutes(5);
            }

            if (NugetVersions.TryGetValue(Name, out var result)) return result;

            lock (NugetVersions)
            {
                try
                {
                    result = $"https://www.nuget.org/packages/{Name}/".AsUri()
                    .Download().GetAwaiter().GetResult().ToLower()
                        .Substring($"<meta property=\"og:title\" content=\"{Name.ToLower()} ", "\"", inclusive: false);
                }
                catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("429"))
                {
                    return Version;
                }

                if (result.IsEmpty()) throw new Exception("Failed to find latest nuget version for " + Name);
                return NugetVersions[Name] = result;
            }
        }

        public void Update()
        {
            var dir = Service.SolutionFolder.AsDirectory().GetSubDirectory(Project.Path()).FullName;
            Service.LogMessage($"Updating nuget package:\n{dir}> PackageName: {Name} Version: {Latest}");

            try
            {
                var projectFullAddress = dir.AsDirectory().GetFiles("*.csproj").First().FullName;
                UpdateCsProjFile(projectFullAddress);
            }
            catch (Exception e)
            {
                Service.LogMessage($"nuget update error on [{Project} -> {Name} ({Version} to {Latest})] :",
                    e.Message);
                return;
            }

            Version = Latest;
        }

        public void UpdateCsProjFile(string projectFullAddress)
        {
            var projectXML = XElement.Load(projectFullAddress);
            IEnumerable<XElement> packageReference = null;
            packageReference = projectXML.Descendants("PackageReference");
            var referenceToUpdate = packageReference.Where(x => x.Attribute("Include").Value == Name);
            var node = referenceToUpdate.Single();
            node.Attribute("Version").Value = Latest;
            projectXML.Save(projectFullAddress);
        }
    }
}