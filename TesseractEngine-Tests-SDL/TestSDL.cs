using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract.SDL;

namespace Tesseract.Tests.SDL {

	public class TestSDL {

		public static void RunWithSDL(Action act) {
			Console.WriteLine("[SDL] Initialization");
			SDL2.Init(SDLSubsystems.Everything);
			try {
				act();
			} finally {
				Console.WriteLine("[SDL] Termination");
				SDL2.Quit();
			}
		}

		public static void TestRaw() => RunWithSDL(() => {
			(SDLWindow window, SDLRenderer renderer) = SDL2.CreateWindowAndRenderer(800, 600, SDLWindowFlags.Shown);

			bool running = true;
			while (running) {
				SDLEvent? evt = SDL2.WaitEventTimeout(10);
				if (evt != null) {
					do {
						switch(evt.Value.Type) {
							case SDLEventType.Quit:
								running = false;
								break;
							default:
								Console.WriteLine($"[SDL] Event: {evt.Value.Type}");
								break;
						}
					} while ((evt = SDL2.PollEvent()) != null);
				}

				renderer.DrawColor = new(0, 0, 0, 0xFF);
				renderer.Clear();
				renderer.DrawColor = new(0xFF, 0xFF, 0xFF, 0xFF);
				renderer.DrawLine(400, 0, 400, 600);
				renderer.DrawLine(0, 300, 800, 300);
				renderer.Present();
			}

			window.Dispose();
		});

	}

}
