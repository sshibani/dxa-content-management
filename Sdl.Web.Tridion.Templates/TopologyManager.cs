﻿using System;
using System.Linq;
using System.Net;
using Tridion.ContentManager.CommunicationManagement;
using Tridion.TopologyManager.Client;

namespace Sdl.Web.Tridion
{
    /// <summary>
    /// Wrapper class for access to Topology Manager.
    /// </summary>
    internal static class TopologyManager
    {
        internal static string GetCmWebsiteUrl()
        {
            CmEnvironmentData cmEnvironment = TopologyManagerClient.CmEnvironments.Where(env => env.Id == TopologyManagerClient.ContentManagerEnvironmentId).FirstOrDefault();
            if (cmEnvironment == null)
            {
                throw new Exception("Unable to obtain CM Environment Data from Topology Manager. CM Environment ID: " + TopologyManagerClient.ContentManagerEnvironmentId);
            }

            return cmEnvironment.WebsiteRootUrl;
        }

        internal static string GetSearchQueryUrl(Publication publication, string environmentPurpose)
        {
            string publicationId = publication.Id.ToString();
            MappingData mapping = TopologyManagerClient.Mappings.Expand("CdEnvironment")
                .Where(m => m.PublicationId == publicationId && m.EnvironmentPurpose == environmentPurpose).FirstOrDefault();
            if (mapping == null || mapping.CdEnvironment == null)
            {
                return null;
            }

            string dxaSearchQueryUrl =  mapping.CdEnvironment.ExtensionProperties
                .Where(ep => ep.Name == "DXA.Search.QueryURL")
                .Select(ep => ep.Value)
                .FirstOrDefault();

            return dxaSearchQueryUrl;
        }

        private static TopologyManagerClient TopologyManagerClient
        {
            get
            {
                return new TopologyManagerClient
                {
                    Credentials = CredentialCache.DefaultNetworkCredentials
                };
            }
        }
    }
}
