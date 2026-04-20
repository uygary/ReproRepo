using System;
using OpenXrUsage.Core.Localization;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using static System.Net.Mime.MediaTypeNames;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;
using Silk.NET.Core.Native;
using System.Diagnostics;
using OpenXrUsage.Core.Infrastructure;

namespace OpenXrUsage.Core
{
    /// <summary>
    /// The main class for the game, responsible for managing game components, settings, 
    /// and platform-specific configurations.
    /// </summary>
    public sealed class OpenXrUsageGame : Game
    {
        // Resources for drawing.
        private readonly GraphicsDeviceManager _graphicsDeviceManager;

        /// <summary>
        /// Indicates if the game is running on a mobile platform.
        /// </summary>
        public readonly static bool IsMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        /// <summary>
        /// Indicates if the game is running on a desktop platform.
        /// </summary>
        public readonly static bool IsDesktop = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        #region Demo Stuff
        private readonly Matrix _viewMatrix = Matrix.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.Up);
        private Matrix _projectionMatrix;
        private SpriteBatch _spriteBatch;
        private Matrix _nearerCubeWorldMatrix;
        private Matrix _fartherCubeWorldMatrix;
        private Model _model;
        private float _rotationAngle;
        #endregion Demo Stuff

        #region VR Stuff
        private OpenXrContext _xrContext;
        private VrProjector _vrProjector;
        #endregion VR Stuff

        /// <summary>
        /// Initializes a new instance of the game. Configures platform-specific settings, 
        /// initializes services like settings and leaderboard managers, and sets up the 
        /// screen manager for screen transitions.
        /// </summary>
        public OpenXrUsageGame()
        {
            _graphicsDeviceManager = new GraphicsDeviceManager(this);

            // Share GraphicsDeviceManager as a service.
            Services.AddService(typeof(GraphicsDeviceManager), _graphicsDeviceManager);

            Content.RootDirectory = "Content";

            // Configure screen orientations.
            _graphicsDeviceManager.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
        }

        /// <summary>
        /// Initializes the game, including setting up localization and adding the 
        /// initial screens to the ScreenManager.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            _projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(45),
                GraphicsDevice.Viewport.AspectRatio,
                0.1f,
                100f);

            InitializeOpenXr();
        }

        private void InitializeOpenXr()
        {
            _xrContext = new OpenXrContext();

            // This is where we get the handles to the graphics backend that OpenXR requires,
            // using the proposed GetNativeHandles function.
            _xrContext.Initialize(GraphicsDevice.GetNativeHandles());
            
            _vrProjector = new VrProjector(_xrContext, GraphicsDevice);
            _vrProjector.Initialize();
            
            IsFixedTimeStep = false;
            _graphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
            _graphicsDeviceManager.ApplyChanges();
        }

        /// <summary>
        /// Loads game content, such as textures and particle systems.
        /// </summary>
        protected override void LoadContent()
        {
            base.LoadContent();

            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _model = Content.Load<Model>("cube");
        }

        /// <summary>
        /// Updates the game's logic, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for game updates.
        /// </param>
        protected override void Update(GameTime gameTime)
        {
            // Exit the game if the Back button (GamePad) or Escape key (Keyboard) is pressed.
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed
                || Keyboard.GetState().IsKeyDown(Keys.Escape))
            {
                Exit();
            }

            var elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _rotationAngle += elapsed;

            _nearerCubeWorldMatrix = Matrix.CreateRotationY(_rotationAngle) * Matrix.CreateRotationX(_rotationAngle * 0.5f);
            _fartherCubeWorldMatrix = _nearerCubeWorldMatrix * Matrix.CreateTranslation(new Vector3(-4f, 3f, -15f));

            _xrContext.PollEvents();

            base.Update(gameTime);
        }

        private void DrawScene(Matrix view, Matrix projection)
        {
            // Draw the farther away cube.
            foreach (ModelMesh mesh in _model.Meshes)
            {
                foreach (BasicEffect effect in mesh.Effects)
                {
                    effect.World = _fartherCubeWorldMatrix;
                    effect.View = view;
                    effect.Projection = projection;
                    effect.EnableDefaultLighting();
                }
                mesh.Draw();
            }

            // Draw the nearer cube at the origin.
            foreach (ModelMesh mesh in _model.Meshes)
            {
                foreach (BasicEffect effect in mesh.Effects)
                {
                    effect.World = _nearerCubeWorldMatrix;
                    effect.View = view;
                    effect.Projection = projection;
                    effect.EnableDefaultLighting();
                }
                mesh.Draw();
            }
        }

        /// <summary>
        /// Draws the game's graphics, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for rendering.
        /// </param>
        protected override void Draw(GameTime gameTime)
        {
            if (!_vrProjector.TryBeginFrame(out bool shouldRender))
            {
                GraphicsDevice.Clear(Color.MonoGameOrange);
                DrawScene(_viewMatrix, _projectionMatrix);
                base.Draw(gameTime);
                return;
            }

            if (!shouldRender)
            {
                GraphicsDevice.Clear(Color.MonoGameOrange);
                DrawScene(_viewMatrix, _projectionMatrix);
                base.Draw(gameTime);
                return;
            }

            for (int eye = 0; eye < _vrProjector.ViewCount; eye++)
            {
                _vrProjector.BeginEye(eye, out Matrix view, out Matrix projection);

                DrawScene(view, projection);

                _vrProjector.EndEye(eye);
            }

            _vrProjector.EndDraw();

            if (_vrProjector.ViewCount > 0)
            {
                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
                _spriteBatch.Draw(_vrProjector.GetEyeTexture(0), GraphicsDevice.Viewport.Bounds, Color.White);
                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _vrProjector?.Dispose();
                _xrContext?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}