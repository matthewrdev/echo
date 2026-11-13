using Echo.CodeStyle;
using Echo.Composition;

using var composition = new MefComposition(typeof(SourceConventionChecker).Assembly);
return composition.Resolve<SourceConventionChecker>().Check(args.Single());
