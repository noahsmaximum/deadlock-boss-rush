// .NET 10's ImplicitUsings pulls in System.Threading, which defines its own ITimer and
// collides with Deadworks' timer interface. Alias the simple name to the one we always mean.
// (An alias directive takes precedence over the wildcard namespace import.)
global using ITimer = DeadworksManaged.Api.ITimer;
