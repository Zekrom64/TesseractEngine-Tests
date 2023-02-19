using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tesseract.Core.Graphics;
using Tesseract.Core.Input;
using Tesseract.Core.Numerics;
using Tesseract.Core.Services;
using Tesseract.GLFW;
using Tesseract.GLFW.Services;

namespace Tesseract.Tests {
	
	public static class TestGLFW {

		public static void RunWithGLFW(Action act) {
			Console.WriteLine("[GLFW] Initialization");
			GLFW3.ErrorCallback = (int err, string desc) => throw new InvalidOperationException("GLFW Error: " + desc);
			GLFW3.Init();
			try {
				act();
			} finally {
				Console.WriteLine("[GLFW] Termination");
				GLFW3.Terminate();
			}
		}

		public static void TestRaw() => RunWithGLFW(() => {
			Console.WriteLine("[GLFW] Raw API - Window");
			GLFW3.DefaultWindowHints();
			GLFW3.WindowHint(GLFWWindowAttrib.Resizable, 0);
			GLFW3.WindowHint(GLFWWindowAttrib.ScaleToMonitor, 1);
			GLFWWindow window = new(new Vector2i(800, 600), "Test");

			while (!window.ShouldClose) {
				GLFW3.WaitEvents(0.1);
			}

			window.Dispose();
		});

		public static void TestServices() => RunWithGLFW(() => {
			Console.WriteLine("[GLFW] Window/Input Services");
			IWindowSystem windowSystem = new GLFWServiceWindowSystem();
			IInputSystem inputSystem = new GLFWServiceInputSystem();
			IWindow window = windowSystem.CreateWindow("Test", 800, 600, new WindowAttributeList() {
				{ WindowAttributes.Resizable, false }
			});

			while (!window.Closing) {
				inputSystem.RunEvents();
				Thread.Sleep(10);
			}

			window.Dispose();
			inputSystem.Dispose();
		});

	}
}
