using System;
using System.Collections.Generic;
using Tesseract.Tests.GLFW;
using Tesseract.Tests.SDL;
using Tesseract.Tests.OpenGL;
using Tesseract.Tests.Vulkan;
using Tesseract.Tests.ImGui;

namespace Tesseract.Tests {
	static class Program {

		private struct Test {

			public Action TestFunc;

			public string Name;

		}

		static void Main(string[] _) {
			List<Test> tests = new() {
				new() { TestFunc = TestGLFW.TestRaw, Name = "GLFW - Raw API" },
				new() { TestFunc = TestGLFW.TestServices, Name = "GLFW - Services" },

				new() { TestFunc = TestSDL.TestRaw, Name = "SDL2 - Raw API" },

				new() { TestFunc = TestGL.TestRaw, Name = "OpenGL - Raw API" },

				new() { TestFunc = TestVulkan.TestRaw, Name = "Vulkan - Raw API" },

				new() { TestFunc = TestImGui.TestSDL, Name = "ImGui - SDL2 + SDLRenderer" },
				new() { TestFunc = TestImGui.TestGL45, Name = "ImGui - SDL2 + OpenGL 4.5" }
			};
			int ntests = 0;
			int successes = 0;
			foreach(Test test in tests) {
				Console.WriteLine($"Next test is \"{test.Name}\" ");
				Console.WriteLine("Press escape to skip or any other key to continue...");
				var keyInfo = Console.ReadKey();
				if (keyInfo.Key == ConsoleKey.Escape) continue;
				ntests++;
				try {
					test.TestFunc();
					successes++;
				} catch(Exception e) {
					Console.Error.WriteLine($"Test \"{test.Name}\" failed!");
					Console.Error.WriteLine(e);
				}
			}
			Console.WriteLine($"{successes}/{ntests} tests succeeeded");
		}
	}
}
