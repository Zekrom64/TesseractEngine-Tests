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
using System.Diagnostics;

namespace Tesseract.Tests {

	public class ImGuiDiagnosticState {

		private static readonly Stopwatch stopwatch = Stopwatch.StartNew();
		private static readonly double tickPeriodMicrosec = 1000000.0 / Stopwatch.Frequency;

		private bool showAbout = false;
		private bool showDemo = false;
		private bool showMetrics = false;
		private bool showDiags = false;

		private readonly long[] durationBuf = new long[64];
		private long lastRenderDuration = 0;

		private readonly float[] longDurationBuf = new float[64];
		private int longDurationCount = 0;

		private double worstTime = 0;
		private double bestTime = double.MaxValue;

		private bool firstFrame = true;

		public void Render() {
			// Append the previous render duration
			durationBuf.AsSpan()[1..].CopyTo(durationBuf);
			durationBuf[^1] = lastRenderDuration;
			long totalTime = 0;
			foreach (long duration in durationBuf) totalTime += duration;
			totalTime /= durationBuf.Length;
			double totalTimeMicrosec = totalTime * tickPeriodMicrosec;

			if (!firstFrame) {
				if (totalTimeMicrosec > worstTime) worstTime = totalTimeMicrosec;
				if (totalTimeMicrosec < bestTime) bestTime = totalTimeMicrosec;
			} else firstFrame = false;

			// Compute durations per frame as float
			Span<float> durations = stackalloc float[durationBuf.Length];
			for (int i = 0; i < durationBuf.Length; i++) durations[i] = (float)(durationBuf[i] * tickPeriodMicrosec);

			// Prepend to the long duration buf if the counter reaches the limit
			const int longDurationPeriod = 16;
			if (longDurationCount++ == longDurationPeriod) {
				longDurationCount = 0;
				longDurationBuf.AsSpan()[1..].CopyTo(longDurationBuf);
				longDurationBuf[^1] = (float)totalTimeMicrosec;
			}

			// Do actual ImGui rendering

			var im = GImGui.Instance;

			// Generate the main menu bar
			if (im.BeginMainMenuBar()) {
				if (im.BeginMenu("Windows"u8)) {
					if (im.MenuItem("Show About"u8)) showAbout = true;
					if (im.MenuItem("Show Demo"u8)) showDemo = true;
					if (im.MenuItem("Show Metrics"u8)) showMetrics = true;
					if (im.MenuItem("Show Diagnostics"u8)) showDiags = true;
					im.EndMenu();
				}
				im.EndMainMenuBar();
			}

			// Built-in ImGui windows
			if (showAbout) im.ShowAboutWindow(ref showAbout);
			if (showDemo) im.ShowDemoWindow(ref showDemo);
			if (showMetrics) im.ShowMetricsWindow(ref showMetrics);

			// Diagnostics window
			if (showDiags && im.Begin("Diagnostics"u8, ref showDiags)) {
				im.Text($"Render Time: {totalTimeMicrosec:N2} uS");
				im.PlotLines("Render Time Plot (Fast)"u8, durations, scaleMin: 0, scaleMax: 1000);
				im.PlotLines("Render Time Plot (Slow)"u8, longDurationBuf, scaleMin: 0, scaleMax: 1000);
				im.Text($"Best Render Time: {bestTime:N2} uS");
				im.Text($"Worst Render Time: {worstTime:N2} uS");
				im.End();
			}
		}

		private long startTime;

		public void MarkBeginRendering() => startTime = stopwatch.ElapsedTicks;

		public void MarkEndRendering() => lastRenderDuration = stopwatch.ElapsedTicks - startTime;

	}

	public static class TestImGui {

		public static void TestSDL() => Tests.TestSDL.RunWithSDL(() => {
			(SDLWindow window, SDLRenderer renderer) = SDL2.CreateWindowAndRenderer(800, 600, SDLWindowFlags.Shown | SDLWindowFlags.Resizable);
			window.Title = "Test";

			GImGui.Instance = new ImGuiCLI();
			GImGui.CurrentContext = GImGui.CreateContext();

			ImGuiSDL2.Init(window, renderer);
			ImGuiSDLRenderer.Init(renderer);

			ImGuiDiagnosticState state = new();

			bool running = true;
			while (running) {
				Vector2i size = window.Size;

				ImGuiSDL2.NewFrame();
				ImGuiSDLRenderer.NewFrame();
				GImGui.NewFrame();

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

				state.Render();
				GImGui.Render();

				state.MarkBeginRendering();
				renderer.BlendMode = SDLBlendMode.None;
				renderer.DrawColor = new(0, 0, 0, 0xFF);
				renderer.Clear();

				ImGuiSDLRenderer.RenderDrawData(GImGui.GetDrawData());
				renderer.Flush();
				state.MarkEndRendering();

				renderer.Present();
			}

			ImGuiSDLRenderer.Shutdown();
			ImGuiSDL2.Shutdown();

			renderer.Dispose();
			window.Dispose();
		});

		public static void TestGL45() => Tests.TestSDL.RunWithSDL(() => {
			SDLWindow window = new("Test", SDL2.WindowPosCentered, SDL2.WindowPosCentered, 800, 600, SDLWindowFlags.Shown | SDLWindowFlags.Resizable | SDLWindowFlags.OpenGL);
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextMajorVersion, 4);
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextMinorVersion, 5);
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextProfileMask, (int)SDLGLProfile.Core);
#if DEBUG
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextFlags, (int)SDLGLContextFlag.DebugFlag);
#else
			SDL2.Functions.SDL_GL_SetAttribute(SDLGLAttr.ContextNoError, 1);
#endif
			IGLContext glctx = new SDLGLContext(window);
			GL gl = new(glctx);
			GL45 gl45 = gl.GL45!;

			GImGui.Instance = new ImGuiCLI();
			GImGui.CurrentContext = GImGui.CreateContext();

			ImGuiSDL2.Init(window, null);
			ImGuiOpenGL45.PreserveState = false; // We can ignore this to help clean up debugging
			ImGuiOpenGL45.Init(gl);

			ImGuiDiagnosticState state = new();

			bool running = true;
			while (running) {
				Vector2i size = window.Size;

				ImGuiSDL2.NewFrame();
				ImGuiOpenGL45.NewFrame();
				GImGui.NewFrame();

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

				state.Render();
				GImGui.Render();

				state.MarkBeginRendering();
				gl45.Disable(GLCapability.ScissorTest);
				gl45.ColorClearValue = new(0, 0, 0, 1);
				gl45.Clear(GLBufferMask.Color);

				ImGuiOpenGL45.RenderDrawData(GImGui.GetDrawData());
				gl45.Finish();
				state.MarkEndRendering();

				glctx.SwapGLBuffers();
			}

			ImGuiOpenGL45.Shutdown();
			ImGuiSDL2.Shutdown();

			glctx.Dispose();
			window.Dispose();
		});

	}

}
