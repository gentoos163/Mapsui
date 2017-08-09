using System;
using System.Collections.Generic;
using Mapsui.Geometries;
using Mapsui.Logging;
using Mapsui.Providers;
using Mapsui.Styles;
using SkiaSharp;

namespace Mapsui.Rendering.Skia
{
    public static class RasterRenderer
    {
		public static void Draw (SKCanvas canvas, IViewport viewport, IStyle style, IFeature feature,
			IDictionary<object, BitmapInfo> tileCache, long currentIteration)
		{
		    try
		    {
		        var raster = (IRaster)feature.Geometry;

		        BitmapInfo bitmapInfo;

		        if (!tileCache.Keys.Contains(raster))
		        {
		            bitmapInfo = BitmapHelper.LoadBitmap(raster.Data);
		            tileCache[raster] = bitmapInfo;
		        }
		        else
		        {
		            bitmapInfo = tileCache[raster];
		        }

		        bitmapInfo.IterationUsed = currentIteration;
		        tileCache[raster] = bitmapInfo;

		        var boundingBox = feature.Geometry.GetBoundingBox();

		        var priorMatrix = canvas.TotalMatrix;

		        var matrix = CreateRotationMatrix(viewport, boundingBox, priorMatrix);

		        canvas.SetMatrix(matrix);

		        var destination = new BoundingBox(0.0, 0.0, boundingBox.Width, boundingBox.Height);

		        BitmapHelper.RenderRaster(canvas, bitmapInfo.Bitmap, destination.ToSkia());

		        canvas.SetMatrix(priorMatrix);
            }
			catch (Exception ex)
			{
				Logger.Log (LogLevel.Error, ex.Message, ex);
			}
		}

        private static SKMatrix CreateRotationMatrix(IViewport viewport, BoundingBox boundingBox, SKMatrix priorMatrix)
        {
            SKMatrix matrix;

            //The front-end sets up the canvas with a matrix based on screen scaling (e.g. retina).
            //We need to retain that effect by combining our matrix with the incoming matrix.

            //We'll create four matrices in addition to the incoming matrix. They perform the
            //zoom scale, focal point offset, user rotation and finally, centering in the screen.

            var userRotation = SKMatrix.MakeRotationDegrees((float) viewport.Rotation);
            var focalPointOffset = SKMatrix.MakeTranslation(
                (float) (boundingBox.Left - viewport.Center.X),
                (float) (viewport.Center.Y - boundingBox.Top));
            var zoomScale = SKMatrix.MakeScale((float) (1.0 / viewport.Resolution), (float) (1.0 / viewport.Resolution));
            var centerInScreen = SKMatrix.MakeTranslation((float) (viewport.Width / 2.0), (float) (viewport.Height / 2.0));

            //We'll concatenate them like so: incomingMatrix * centerInScreen * userRotation * zoomScale * focalPointOffset

            SKMatrix.Concat(ref matrix, zoomScale, focalPointOffset);
            SKMatrix.Concat(ref matrix, userRotation, matrix);
            SKMatrix.Concat(ref matrix, centerInScreen, matrix);
            SKMatrix.Concat(ref matrix, priorMatrix, matrix);

            return matrix;
        }
    }
}
