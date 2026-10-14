// Cosmos 3.0.85 uses Keyboard.KeyEvent/ConsoleKeyEx; Cosmos 3.0.94
// uses Input.KeyEvent/Key. All GUI handlers must use the real Cosmos event.
#if ZONDERQ_COSMOS_INPUT_API
global using Cosmos.Kernel.System.Input;
global using ConsoleKeyEx = Cosmos.Kernel.System.Input.Key;
#else
global using Cosmos.Kernel.System.Keyboard;
#endif
