namespace BigRedProf.Stories.StoriesCli
{
	public enum ThingFormat
	{
		RawCode = 1,
		ModelWithSchema = 2,

		/// <summary>
		/// The thing is a model of one known schema, named by --thingSchemaId. Apps that
		/// wrap every thing in an envelope of their own (digihouse, for example) record
		/// things this way rather than as a bare <see cref="ModelWithSchema"/>.
		/// </summary>
		Model = 3
	}
}
