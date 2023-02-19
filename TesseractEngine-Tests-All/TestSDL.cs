using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract.Core.Numerics;
using Tesseract.SDL;

namespace Tesseract.Tests {

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
			(SDLWindow window, SDLRenderer renderer) = SDL2.CreateWindowAndRenderer(800, 600, SDLWindowFlags.Shown | SDLWindowFlags.Resizable);

			bool running = true;
			while (running) {
				Vector2i sz = renderer.OutputSize;
				Vector2i hsz = sz / 2;

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
				renderer.DrawLine(hsz.X, 0, hsz.X, sz.Y);
				renderer.DrawLine(0, hsz.Y, sz.X, hsz.Y);
				renderer.Present();
			}

			renderer.Dispose();
			window.Dispose();
		});

	}

}
