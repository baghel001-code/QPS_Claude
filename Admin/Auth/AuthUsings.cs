// The auth module hashes and verifies passwords with the application's own IPasswordHasher
// (registered in Program.cs: builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>()).
// If the build says this type can't be found, change the namespace below to the one that
// contains your IPasswordHasher interface. It is the only place the module names it.
global using AppPasswordHasher = Application.Interfaces.V1.User.IPasswordHasher;
