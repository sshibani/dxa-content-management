﻿using Sdl.Web.DataModel;
using Sdl.Web.Tridion.Common;
using Tridion.ContentManager.Publishing.Rendering;
using Tridion.ContentManager.Templating;
using Tridion.ContentManager.Templating.Assembly;

namespace Sdl.Web.Tridion.Templates
{
    /// <summary>
    /// Generates a DXA 2 data model based on the current Page
    /// </summary>
    [TcmTemplateTitle("Generate DXA 2 Page Model")]
    [TcmTemplateParameterSchema("resource:Sdl.Web.Tridion.Resources.GenerateDynamicPageParameters.xsd")]
    public class GeneratePageModel : TemplateBase
    {
        /// <summary>
        /// Performs the Transform.
        /// </summary>
        public override void Transform(Engine engine, Package package)
        {
            Logger.Debug("Transform");

            int expandLinkDepth;
            package.TryGetParameter("expandLinkDepth", out expandLinkDepth, Logger);

            RenderedItem renderedItem = Engine.PublishingContext.RenderedItem;

            Dxa2ModelBuilderSettings settings = new Dxa2ModelBuilderSettings
            {
                ExpandLinkDepth = expandLinkDepth
            };

            Dxa2ModelBuilder modelBuilder = new Dxa2ModelBuilder(
                Session, 
                settings, 
                mmc => renderedItem.AddBinary(mmc).Url,
                (stream, fileName, relatedComponent, mimeType) => renderedItem.AddBinary(stream, fileName, string.Empty, relatedComponent, mimeType).Url
                );
            PageModelData pageModel = modelBuilder.BuildPageModel(GetPage());

            string pageModelJson = JsonSerialize(pageModel, DataModelBinder.SerializerSettings);
            Item outputItem = Package.CreateStringItem(ContentType.Text, pageModelJson);
            Package.PushItem(Package.OutputName, outputItem);
        }
    }
}
