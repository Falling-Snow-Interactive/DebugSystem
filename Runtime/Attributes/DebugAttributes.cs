using System;
using JetBrains.Annotations;

namespace Fsi.Debug.Attributes
{
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	[UsedImplicitly]
	public sealed class DebugClassAttribute : Attribute
	{
		public string DisplayName { get; }
		public int Order { get; }
		public string Category { get; }
		
		public DebugClassAttribute(string displayName = null, int order = 0, string category = null)
		{
			DisplayName = displayName;
			Order = order;
			Category = category;
		}
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
	[UsedImplicitly]
	public sealed class DebugPropertyAttribute : Attribute
	{
		public string DisplayName { get; }
		public int Order { get; }
		public string Category { get; }
		public bool ReadOnly { get; }
		
		public DebugPropertyAttribute(string displayName = null, int order = 0, string category = null, bool readOnly = false)
		{
			DisplayName = displayName;
			Order = order;
			Category = category;
			ReadOnly = readOnly;
		}
	}

	[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
	[UsedImplicitly]
	public sealed class DebugMethodAttribute : Attribute
	{
		public string DisplayName { get; }
		public int Order { get; }
		public string Category { get; }
		
		public DebugMethodAttribute(string displayName = null, int order = 0, string category = null)
		{
			DisplayName = displayName;
			Order = order;
			Category = category;
		}
	}
}
