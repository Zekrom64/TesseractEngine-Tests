using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract.OpenGL;
using Tesseract.SDL;
using Tesseract.SDL.Services;
using Tesseract.ImGui;
using Tesseract.ImGui.SDL;
using Tesseract.ImGui.OpenGL;
using Tesseract.CLI.ImGui;
using Tesseract.Core.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Tesseract.Tests.ImGui {

	public static class TestImGui {

		private static bool enableDemoWindow = false;

		private static void TestImpl() {
			GImGui.NewFrame();
			if (GImGui.BeginMainMenuBar()) {
				if (GImGui.BeginMenu("Show")) {
					GImGui.Checkbox("Demo Window", ref enableDemoWindow);
					GImGui.EndMenu();
				}
				GImGui.EndMainMenuBar();
			}
			if (enableDemoWindow) GImGui.ShowDemoWindow(ref enableDemoWindow);
			GImGui.Render();
		}

		public static void TestSDL() => SDL.TestSDL.RunWithSDL(() => {
			(SDLWindow window, SDLRenderer renderer) = SDL2.CreateWindowAndRenderer(800, 600, SDLWindowFlags.Shown | SDLWindowFlags.Resizable);
			window.Title = "Test";

			GImGui.Instance = new ImGuiCLI();
			GImGui.CurrentContext = GImGui.CreateContext();

			ImGuiSDL2.Init(window, renderer);
			ImGuiSDLRenderer.Init(renderer);

			SDLTexture? fontTexture = null;

			bool running = true;
			while (running) {
				Vector2i size = window.Size;

				ImGuiSDL2.NewFrame();
				ImGuiSDLRenderer.NewFrame();

				SDLEvent? evt = SDL2.WaitEventTimeout(10);
				if (evt != null) {
					do {
						if (!ImGuiSDL2.ProcessEvent(evt.Value)) {
							SDLEvent e = evt.Value;
							switch(e.Type) {
								case SDLEventType.Quit:
									running = false;
									break;
							}
						}
					} while ((evt = SDL2.PollEvent()) != null);
				}

				TestImpl();

				renderer.BlendMode = SDLBlendMode.None;
				renderer.DrawColor = new(0, 0, 0, 0xFF);
				renderer.Clear();

				renderer.BlendMode = SDLBlendMode.Blend;

				renderer.DrawColor = new Vector4b(0xFF, 0, 0, 0xFF);
				renderer.FillRect(new SDLRect() { Size = size / 2 });

				if (fontTexture == null) {
					fontTexture = new((IntPtr)(nint)GImGui.IO.Fonts.TexID);
					ReadOnlySpan<byte> pixels = GImGui.IO.Fonts.GetTexDataAsRGBA32(out int w, out int h, out int _);
					Image.LoadPixelData<Rgba32>(pixels, w, h).SaveAsPng("atlas.png");
				}
				ImGuiSDLRenderer.RenderDrawData(GImGui.GetDrawData());

				renderer.Present();
			}

			ImGuiSDLRenderer.Shutdown();
			ImGuiSDL2.Shutdown();

			renderer.Dispose();
			window.Dispose();
		});

		public static void TestGL45() => SDL.TestSDL.RunWithSDL(() => {
			SDLWindow window = new("Test", SDL2.WindowPosCentered, SDL2.WindowPosCentered, 800, 600, SDLWindowFlags.Shown | SDLWindowFlags.Resizable | SDLWindowFlags.OpenGL);
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextMajorVersion, 4);
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextMinorVersion, 5);
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextProfileMask, 0x0001); // Core
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextFlags, 0x0001); // Debug
			IGLContext glctx = new SDLGLContext(window);
			GL gl = new(glctx);
			GL45 gl45 = gl.GL45!;

			GImGui.Instance = new ImGuiCLI();
			GImGui.CurrentContext = GImGui.CreateContext();

			ImGuiSDL2.Init(window, null);
			ImGuiOpenGL45.PreserveState = false; // We can ignore this to help clean up debugging
			ImGuiOpenGL45.Init(gl);

			bool running = true;
			while (running) {
				Vector2i size = window.Size;

				ImGuiSDL2.NewFrame();
				ImGuiOpenGL45.NewFrame();

				SDLEvent? evt = SDL2.WaitEventTimeout(10);
				if (evt != null) {
					do {
						if (!ImGuiSDL2.ProcessEvent(evt.Value)) {
							SDLEvent e = evt.Value;
							switch (e.Type) {
								case SDLEventType.Quit:
									running = false;
									break;
							}
						}
					} while ((evt = SDL2.PollEvent()) != null);
				}

				TestImpl();

				gl45.Disable(GLCapability.ScissorTest);
				gl45.ColorClearValue = new(1, 0, 0, 1);
				gl45.Clear(GLBufferMask.Color);

				ImGuiOpenGL45.RenderDrawData(GImGui.GetDrawData());

				glctx.SwapGLBuffers();
			}

			ImGuiOpenGL45.Shutdown();
			ImGuiSDL2.Shutdown();

			glctx.Dispose();
			window.Dispose();
		});

	}

}
