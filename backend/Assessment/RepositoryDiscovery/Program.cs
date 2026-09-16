using ArmMigrationAssist.RepositoryDiscovery;

if (args.Length > 0 && args[0].Equals("serve", StringComparison.OrdinalIgnoreCase))
{
	var app = AssessmentApi.Build(args[1..]);
	await app.RunAsync();
	return 0;
}

return await RepositoryDiscoveryCommand.RunAsync(args);