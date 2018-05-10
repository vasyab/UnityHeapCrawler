namespace UnityHeapCrawler
{
	/// <summary>
	/// Order for crawling groups
	/// </summary>
	public enum CrawlOrder
	{
		UserRoots,
		StaticFields,
		Hierarchy,
		SriptableObjects,
		Prefabs,
		UnityObjects
	}
}