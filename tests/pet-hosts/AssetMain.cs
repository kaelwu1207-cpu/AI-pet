public static class AssetMain {public static int Main(string[] args){try{AssetTests.Run(args[0]);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}}
