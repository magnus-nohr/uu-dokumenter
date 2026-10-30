using UuDok.Tools;

// Verktøy for syntetisk testkorpus. Bruk:
//   UuDok.Tools images <mappe>       – testbilder (KI-tittelbilde, graf, skjermbilde, QR …)
//   UuDok.Tools crafted <mappe>      – SDK-genererte kanttilfeller og ondsinnede filer
//   UuDok.Tools inject <inn> <ut>    – legg ukjente utvidelser inn i en eksisterende pakke
//   UuDok.Tools big <mappe> [n]      – stor presentasjon (standard 400 lysbilder)
var cmd = args.FirstOrDefault();
switch (cmd)
{
    case "images": Images.WriteAll(args[1]); break;
    case "crafted": Crafted.WriteAll(args[1], Path.Combine(args[1], "img")); break;
    case "inject": Crafted.InjectUnknown(args[1], args[2]); break;
    case "big": Crafted.BigPresentation(Path.Combine(args[1], "stor-400.pptx"), Path.Combine(args[1], "img"), args.Length > 2 ? int.Parse(args[2]) : 400); break;
    case "process": return await Cli.Process(args[1], args[2], args.Skip(3).ToArray());
    case "eval-ai": return await Eval.Run(args.Skip(1).ToArray());
    case "regler": RulesDoc.Write(args[1]); break;
    default:
        Console.Error.WriteLine("Ukjent kommando. Se kommentar i Program.cs.");
        return 1;
}
Console.WriteLine("Ferdig.");
return 0;
