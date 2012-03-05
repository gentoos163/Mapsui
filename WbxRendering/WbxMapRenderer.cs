﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BruTile;
using BruTile.Cache;
using SharpMap;
using SharpMap.Geometries;
using SharpMap.Layers;
using SharpMap.Providers;
using SharpMap.Rendering;
using System.Windows.Controls;

namespace WbxRendering
{
    public class WbxMapRenderer : IRenderer
    {
        WriteableBitmap targetBitmap;
        Canvas target;

        public WbxMapRenderer(Canvas target)
        {
            this.target = target;

            if (target.ActualWidth == 0 || target.ActualHeight == 0) return;
            target.Children.Add(InitializeBitmap((int)target.ActualWidth, (int)target.ActualHeight));
        }

        private Image InitializeBitmap(int width, int height)
        {
            targetBitmap = BitmapFactory.New(width, height);
            var image = new Image();
            image.Source = targetBitmap;
            return image;
        }

        public void Render(IView view, LayerCollection layers)
        {
            if (targetBitmap == null ||
                targetBitmap.PixelWidth != (int)target.ActualWidth ||
                targetBitmap.PixelHeight != (int)target.ActualHeight)
            {
                target.Arrange(new Rect(0, 0, view.Width, view.Height));
                if (target.ActualWidth <= 0 || target.ActualHeight <= 0) return; 
                target.Children.Clear();
                target.Children.Add(InitializeBitmap((int)target.ActualWidth, (int)target.ActualHeight));
            }

            targetBitmap.Clear(Colors.White);

            foreach (var layer in layers)
            {
                if (layer.Enabled &&
                    layer.MinVisible <= view.Resolution &&
                    layer.MaxVisible >= view.Resolution)
                {
                    RenderLayer(targetBitmap, view, layer);
                }
            }

            target.Arrange(new Rect(0, 0, view.Width, view.Height));
        }

        private static void RenderLayer(WriteableBitmap targetBitmap, IView view, ILayer layer)
        {
            if (layer.Enabled == false) return;

            if (layer is LabelLayer)
            {
                // not supported
            }
            else if (layer is ITileLayer)
            {
                var tileLayer = (ITileLayer)layer;
                RenderTileLayer(targetBitmap, tileLayer.Schema, view, tileLayer.MemoryCache, layer.Opacity);
            }
            else
            {
                //not supported
            }
        }

        private static void RenderTileLayer(WriteableBitmap targetBitmap, ITileSchema schema, IView view, MemoryCache<Feature> memoryCache, double opacity)
        {
            int level = Utilities.GetNearestLevel(schema.Resolutions, view.Resolution);
            DrawRecursive(targetBitmap, schema, view, memoryCache, view.Extent.ToExtent(), level, opacity);
        }

        private static void DrawRecursive(WriteableBitmap targetBitmap, ITileSchema schema, IView view, MemoryCache<Feature> memoryCache, Extent extent, int level, double opacity)
        {
            var tileInfos = schema.GetTilesInView(extent, level);

            foreach (TileInfo tile in tileInfos)
            {
                var feature = memoryCache.Find(tile.Index);
                if (feature == null)
                {
                    if (level > 0) DrawRecursive(targetBitmap, schema, view, memoryCache, tile.Extent.Intersect(extent), level - 1, opacity);
                }
                else
                {
                    var renderedGeometry = feature.RenderedGeometry as WriteableBitmap;
                    if (renderedGeometry == null) // create
                    {
                        var image = ((IRaster)feature.Geometry).Data;
                        var bitmap = LoadBitmap(image);
                        Rect dest = WorldToMap(tile.Extent, view);
                        DrawImage(targetBitmap, bitmap, dest, tile, memoryCache, opacity);
                        feature.RenderedGeometry = bitmap;
                    }
                    else // position
                    {
                        var bitmap = (WriteableBitmap)feature.RenderedGeometry;
                        Rect dest = WorldToMap(tile.Extent, view);
                        DrawImage(targetBitmap, bitmap, dest, tile, memoryCache, opacity);
                    }
                }
            }
        }

        private static Rect WorldToMap(Extent extent, IView view)
        {
            SharpMap.Geometries.Point min = view.WorldToView(extent.MinX, extent.MinY);
            SharpMap.Geometries.Point max = view.WorldToView(extent.MaxX, extent.MaxY);
            return new Rect(min.X, max.Y, max.X - min.X, min.Y - max.Y);
        }

        private static void DrawImage(WriteableBitmap targetBitmap, WriteableBitmap bitmap, Rect dest, TileInfo tile, MemoryCache<Feature> memoryCache, double opacity)
        {
            try
            {
                //todo: look at GDI rendering to deal with clipping
                                var destRounded = RoundToPixel(dest);
                var sourceRect = new Rect(0, 0, 256, 256);

                opacity = 1; // hack, opacity not supported 
                // Create the opacity color which sets the transparencey of the blitted image.
                var color = Color.FromArgb((byte)Convert.ToInt32(255 * opacity), 255, 255, 255);
                //todo: rethink opacity. There is opacity at layer level, and could be at tile level.

                targetBitmap.Blit(destRounded, bitmap, sourceRect, color, WriteableBitmapExtensions.BlendMode.Alpha);
                targetBitmap.DrawRectangle((int)destRounded.Left, (int)destRounded.Top, (int)destRounded.Right, (int)destRounded.Bottom, Colors.Red);
            }
            catch (Exception ex)
            {
                // todo report error
                Console.WriteLine(ex.Message);
                memoryCache.Remove(tile.Index);
            }
        }
        
        private static WriteableBitmap LoadBitmap(MemoryStream memoryStream)
        {
            var img = new BitmapImage { CreateOptions = BitmapCreateOptions.None };
            memoryStream.Position = 0;
            img.BeginInit();
            img.StreamSource = memoryStream;
            img.EndInit();
            return LocalBitmapFactory.New(img);
        }

        public static Rect RoundToPixel(Rect dest)
        {
            // To get seamless aligning you need to round the 
            // corner coordinates to pixel. The new width and
            // height will be a result of that.
            dest = new Rect(
                Math.Round(dest.Left),
                Math.Round(dest.Top),
                (Math.Round(dest.Right) - Math.Round(dest.Left)),
                (Math.Round(dest.Bottom) - Math.Round(dest.Top)));
            return dest;
        }
    }

    public static class LocalBitmapFactory
    {
        public static WriteableBitmap New(BitmapSource source)
        {
#if SILVERLIGHT
          return new WriteableBitmap(source);
#else
            FormatConvertedBitmap formatedBitmapSource = new FormatConvertedBitmap();
            formatedBitmapSource.BeginInit();
            formatedBitmapSource.Source = source;
            formatedBitmapSource.DestinationFormat = PixelFormats.Bgra32;
            formatedBitmapSource.EndInit();

            return new WriteableBitmap(formatedBitmapSource);
#endif
        }
    }
}
