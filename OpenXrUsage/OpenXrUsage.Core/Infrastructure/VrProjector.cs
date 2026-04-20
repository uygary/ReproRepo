using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Silk.NET.OpenXR;
using System;

namespace OpenXrUsage.Core.Infrastructure
{
    public sealed class VrProjector : IDisposable
    {
        private readonly OpenXrContext _xrContext;
        private readonly GraphicsDevice _graphicsDevice;
        private RenderTarget2D[][] _eyeRTs;
        private CompositionLayerProjectionView[] _projViews;

        public int ViewCount => _xrContext.ViewCount;

        public Texture2D GetEyeTexture(int eye) =>
            _eyeRTs[eye][_xrContext.GetCurrentImageIndex(eye)];

        public VrProjector(OpenXrContext xrContext, GraphicsDevice graphicsDevice)
        {
            _xrContext = xrContext;
            _graphicsDevice = graphicsDevice;
        }

        public void Initialize()
        {
            _eyeRTs = new RenderTarget2D[_xrContext.ViewCount][];

            for (int eye = 0; eye < _xrContext.ViewCount; eye++)
            {
                int imageCount = _xrContext.GetSwapchainImageCount(eye);
                _eyeRTs[eye] = new RenderTarget2D[imageCount];
                
                for (int i = 0; i < imageCount; i++)
                {
                    // This is where we wrap the OpenXR swap-chain images in MonoGame RenderTarget2D instances,
                    // using the proposed FromNativeImage factory function.
                    _eyeRTs[eye][i] = RenderTarget2D.FromNativeImage(
                        _graphicsDevice,
                        _xrContext.GetSwapchainImageHandle(eye, i),
                        (int)_xrContext.ViewConfigViews[eye].RecommendedImageRectWidth,
                        (int)_xrContext.ViewConfigViews[eye].RecommendedImageRectHeight
                    );
                }
            }
        }

        public unsafe bool TryBeginFrame(out bool shouldRender)
        {
            shouldRender = false;
            
            if (!_xrContext.IsSessionRunning || !_xrContext.BeginFrame())
            {
                return false;
            }

            if (!_xrContext.ShouldRender)
            {
                _xrContext.EndFrame(null, 0, false);
                return true;
            }

            _xrContext.LocateViews();
            _projViews = new CompositionLayerProjectionView[_xrContext.ViewCount];
            shouldRender = true;
            return true;
        }

        public void BeginEye(int eye, out Matrix view, out Matrix projection)
        {
            _xrContext.AcquireSwapchainImage(eye);
            
            int imageIndex = _xrContext.GetCurrentImageIndex(eye);
            var rt = _eyeRTs[eye][imageIndex];
            
            _graphicsDevice.SetRenderTarget(rt);
            _graphicsDevice.Clear(Color.MonoGameOrange);
            
            var xrView = _xrContext.Views[eye];
            
            float nearPlane = 0.1f, farPlane = 100f;
            float left = MathF.Tan(xrView.Fov.AngleLeft) * nearPlane;
            float right = MathF.Tan(xrView.Fov.AngleRight) * nearPlane;
            float bottom = MathF.Tan(xrView.Fov.AngleDown) * nearPlane;
            float top = MathF.Tan(xrView.Fov.AngleUp) * nearPlane;
            projection = Matrix.CreatePerspectiveOffCenter(left, right, bottom, top, nearPlane, farPlane);
            
            var pos = new Vector3(xrView.Pose.Position.X, xrView.Pose.Position.Y, xrView.Pose.Position.Z);
            var rot = new Quaternion(xrView.Pose.Orientation.X, xrView.Pose.Orientation.Y, xrView.Pose.Orientation.Z, xrView.Pose.Orientation.W);
            
            view = Matrix.Invert(Matrix.CreateFromQuaternion(rot) * Matrix.CreateTranslation(pos)) * Matrix.CreateTranslation(0, 0, -5);

            _projViews[eye] = new CompositionLayerProjectionView
            {
                Type = StructureType.CompositionLayerProjectionView,
                Pose = xrView.Pose,
                Fov = xrView.Fov,
                SubImage = new SwapchainSubImage
                {
                    Swapchain = _xrContext.Swapchains[eye],
                    ImageRect = new Rect2Di
                    {
                        Offset = new Offset2Di
                        {
                            X = 0,
                            Y = 0,
                        },
                        Extent = new Extent2Di
                        {
                            Width = rt.Width,
                            Height = rt.Height,
                        }
                    },
                },
            };
        }

        public void EndEye(int eye)
        {
            _graphicsDevice.SetRenderTarget(null);
            _xrContext.ReleaseSwapchainImage(eye);
        }

        public unsafe void EndDraw()
        {
            fixed (CompositionLayerProjectionView* projViewsPtr = _projViews)
            {
                _xrContext.EndFrame(projViewsPtr, _xrContext.ViewCount, true);
            }
        }

        public void Dispose()
        {
            for (var eye = 0; eye < _eyeRTs?.Length; eye++)
            {
                for (var i = 0; i < _eyeRTs[eye]?.Length; i++)
                {
                    _eyeRTs[eye][i]?.Dispose();
                }
            }
        }
    }
}
