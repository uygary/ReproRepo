using System;
using DrawUserIndexedPrimitivesCrash.Core.Localization;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using static System.Net.Mime.MediaTypeNames;

namespace DrawUserIndexedPrimitivesCrash.Core
{
    /// <summary>
    /// The main class for the game, responsible for managing game components, settings, 
    /// and platform-specific configurations.
    /// </summary>
    public class DrawUserIndexedPrimitivesCrashGame : Game
    {

        private readonly GraphicsDeviceManager _graphicsManager;

        private VertexPositionColorTexture[] _verts;
        private int[] _indices;

        private SpriteEffect _testEffect;
        private Texture2D _defaultTexture;

        public DrawUserIndexedPrimitivesCrashGame()
        {
            _graphicsManager = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            IsFixedTimeStep = false;
        }

        protected override void Initialize()
        {
            int width = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
            int height = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;

            _graphicsManager.PreferredBackBufferWidth = width;
            _graphicsManager.PreferredBackBufferHeight = height;
            _graphicsManager.SynchronizeWithVerticalRetrace = true;
            _graphicsManager.GraphicsProfile = GraphicsProfile.HiDef;

            _graphicsManager.ApplyChanges();

            base.Initialize();

            _verts = new VertexPositionColorTexture[2];
            _verts[0].Position = new Vector3(0, 20, 0);
            _verts[0].Color = Color.Red;
            _verts[0].TextureCoordinate = new Vector2(0, 0);

            _verts[1].Position = new Vector3(500, 420, 0);
            _verts[1].Color = Color.Green;
            _verts[1].TextureCoordinate = new Vector2(0, 0);

            _indices = new int[2];
            _indices[0] = 0;
            _indices[1] = 1;
        }

        protected override void LoadContent()
        {
            base.LoadContent();

            _defaultTexture = new Texture2D(_graphicsManager.GraphicsDevice, 2, 2) { Name = "DefaultTexture" };
            Color[] pixelData = [Color.White, Color.White, Color.White, Color.White];
            _defaultTexture.SetData(pixelData);

            _testEffect = new SpriteEffect(_graphicsManager.GraphicsDevice);
            _testEffect.Parameters["Texture"].SetValue(_defaultTexture);
            _testEffect.Parameters[0].SetValue(Matrix.Identity);

            _graphicsManager.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            _graphicsManager.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        }

        protected override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            _graphicsManager.GraphicsDevice.Clear(Color.CadetBlue);

            _testEffect.Techniques[0].Passes[0].Apply();

            _graphicsManager.GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.LineList, _verts, 0, 2, _indices, 0, 1);
        }
    }
}