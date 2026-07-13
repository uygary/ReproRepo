using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using VulkanLargeTextures.Core.Localization;
using static System.Net.Mime.MediaTypeNames;

namespace VulkanLargeTextures.Core
{
    /// <summary>
    /// The main class for the game, responsible for managing game components, settings, 
    /// and platform-specific configurations.
    /// </summary>
    public class VulkanLargeTexturesGame : Game
    {
        private static readonly ushort LoadMultiplier = 10;
        
        // Resources for drawing.
        private GraphicsDeviceManager _graphicsDeviceManager;

        /// <summary>
        /// Indicates if the game is running on a mobile platform.
        /// </summary>
        public readonly static bool IsMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        /// <summary>
        /// Indicates if the game is running on a desktop platform.
        /// </summary>
        public readonly static bool IsDesktop = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        private readonly Stopwatch _stopwatch;
        private readonly List<Texture2D> _contentManagers;
        private readonly List<Texture2D> _textures;
        private Rectangle _rectangle;
        private SpriteBatch _spriteBatch;
        private long? _texturesLoadedIn;
        private long? _texturesRenderedIn;
        private long? _presentedIn;

        /// <summary>
        /// Initializes a new instance of the game. Configures platform-specific settings, 
        /// initializes services like settings and leaderboard managers, and sets up the 
        /// screen manager for screen transitions.
        /// </summary>
        public VulkanLargeTexturesGame()
        {
            _graphicsDeviceManager = new GraphicsDeviceManager(this);

            // Share GraphicsDeviceManager as a service.
            Services.AddService(typeof(GraphicsDeviceManager), _graphicsDeviceManager);

            Content.RootDirectory = "Content";

            // Configure screen orientations.
            _graphicsDeviceManager.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;

            _stopwatch = new Stopwatch();
            _contentManagers = new List<Texture2D>(LoadMultiplier);
            _textures = new List<Texture2D>(100_000);
        }

        /// <summary>
        /// Initializes the game, including setting up localization and adding the 
        /// initial screens to the ScreenManager.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            // Load supported languages and set the default language.
            List<CultureInfo> cultures = LocalizationManager.GetSupportedCultures();
            var languages = new List<CultureInfo>();
            for (int i = 0; i < cultures.Count; i++)
            {
                languages.Add(cultures[i]);
            }

            // TODO You should load this from a settings file or similar,
            // based on what the user or operating system selected.
            var selectedLanguage = LocalizationManager.DEFAULT_CULTURE_CODE;
            LocalizationManager.SetCulture(selectedLanguage);

            _graphicsDeviceManager.PreferredBackBufferWidth = 1920;
            _graphicsDeviceManager.PreferredBackBufferHeight = 1080;
            _graphicsDeviceManager.ApplyChanges();

            _spriteBatch = new SpriteBatch(GraphicsDevice);
        }

        /// <summary>
        /// Loads game content, such as textures and particle systems.
        /// </summary>
        protected override void LoadContent()
        {
            base.LoadContent();

            _stopwatch.Start();
            
            var contentRoot = Content.RootDirectory;
            var textureFiles = Directory
                .EnumerateFiles(
                    contentRoot,
                    "*.xnb")
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();

            for (var i = 0; i < LoadMultiplier; i++)
            {
                Console.WriteLine($@"Loading batch {i}...");
                var contentManager = new ContentManager(Services, contentRoot);
                var textures = textureFiles
                    .Select(contentManager.Load<Texture2D>);

                foreach (var texture in textures)
                {
                    _textures.AddRange(texture);
                }
            }

            _stopwatch.Stop();
            _texturesLoadedIn = _stopwatch.ElapsedMilliseconds;
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
                Exit();

            var viewportSize = GraphicsDevice.Viewport.Bounds.Size;
            _rectangle = new Rectangle(0, 0, viewportSize.X, viewportSize.Y);

            base.Update(gameTime);
        }

        /// <summary>
        /// Draws the game's graphics, called once per frame.
        /// </summary>
        /// <param name="gameTime">
        /// Provides a snapshot of timing values used for rendering.
        /// </param>
        protected override void Draw(GameTime gameTime)
        {
            // Clears the screen with the MonoGame orange color before drawing.
            GraphicsDevice.Clear(Color.MonoGameOrange);

            _stopwatch.Restart();
            _spriteBatch.Begin();
            for (var i = 0; i < _textures.Count; i++)
            {
                var texture = _textures[i];
                _spriteBatch.Draw(texture, _rectangle, Color.White * 0.5f);
            }

            base.Draw(gameTime);
            
            _spriteBatch.End();
            _stopwatch.Stop();

            if (!_texturesRenderedIn.HasValue)
            {
                _texturesRenderedIn = _stopwatch.ElapsedMilliseconds;

                Console.WriteLine($@"Textures loaded in: {_texturesLoadedIn} ms");
                Console.WriteLine($@"{_textures.Count} textures rendered in: {_texturesRenderedIn} ms");
            }
            
            _stopwatch.Restart();
        }

        protected override void EndDraw()
        {
            base.EndDraw();

            _stopwatch.Stop();
            if (!_presentedIn.HasValue)
            {
                _presentedIn = _stopwatch.ElapsedMilliseconds;
                Console.WriteLine($@"Presented in: {_presentedIn} ms");
            }
        }
    }
}