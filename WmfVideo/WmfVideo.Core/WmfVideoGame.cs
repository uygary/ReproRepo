using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;

namespace WmfVideo.Core
{
    /// <summary>
    /// The main class for the game, responsible for managing game components, settings, 
    /// and platform-specific configurations.
    /// </summary>
    /// <remarks>
    /// This class is the entry point for the game and handles initialization, content loading,
    /// and screen management.
    /// </remarks>}
    public class WmfVideoGame : Game
    {
        // Resources for drawing.
        private GraphicsDeviceManager graphicsDeviceManager;

        private VideoPlayer _videoPlayer;
        private Video _video;
        private Texture2D _videoTexture;
        private SpriteBatch _spriteBatch;

        /// <summary>
        /// Indicates if the game is running on a mobile platform.
        /// </summary>
        public readonly static bool IsMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        /// <summary>
        /// Indicates if the game is running on a desktop platform.
        /// </summary>
        public readonly static bool IsDesktop = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        /// <summary>
        /// Initializes a new instance of the game. Configures platform-specific settings, 
        /// initializes services like settings and leaderboard managers, and sets up the 
        /// screen manager for screen transitions.
        /// </summary>
        public WmfVideoGame()
        {
            graphicsDeviceManager = new GraphicsDeviceManager(this);

            // Share GraphicsDeviceManager as a service.
            Services.AddService(typeof(GraphicsDeviceManager), graphicsDeviceManager);

            Content.RootDirectory = "Content";

            // Configure screen orientations.
            graphicsDeviceManager.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
        }

        /// <summary>
        /// Initializes the game, including setting up localization and adding the 
        /// initial screens to the ScreenManager.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            _videoPlayer = new VideoPlayer();
            _videoPlayer.Play(_video);
        }

        /// <summary>
        /// Loads game content, such as textures and particle systems.
        /// </summary>
        protected override void LoadContent()
        {
            base.LoadContent();

            _video = Content.Load<Video>("Videos/example-720");
            _spriteBatch = new SpriteBatch(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var keyboardState = Keyboard.GetState();
            if (keyboardState.IsKeyDown(Keys.Escape))
            {
                Exit();
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            // Extract the current video frame as a Texture2D
            if (_videoPlayer.State == MediaState.Stopped)
            {
                _videoPlayer.Play(_video);
            }
            if (_videoPlayer.State != MediaState.Stopped)
            {
                _videoTexture = _videoPlayer.GetTexture();
            }

            // Draw the texture to the screen if it's available
            if (_videoTexture != null)
            {
                _spriteBatch.Begin();
                _spriteBatch.Draw(_videoTexture, GraphicsDevice.Viewport.Bounds, Color.White);
                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }

        protected override void UnloadContent()
        {
            // Always dispose of the player to prevent native memory leaks
            _videoPlayer.Dispose();
            _videoTexture.Dispose();
            _video.Dispose();
            _spriteBatch.Dispose();
            
            base.UnloadContent();
        }
    }
}