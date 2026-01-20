using System;
using System.Collections.Generic;
using System.Reflection;

namespace Fsi.Debug
{
	public enum DebugMemberKind
	{
		Property,
		Field,
		Method,
	}

	public sealed class DebugMemberInfo
	{
		public string Name { get; }
		public string DisplayName { get; }
		public DebugMemberKind Kind { get; }
		public Type ValueType { get; }
		public int Order { get; }
		public string Category { get; }
		public bool ReadOnly { get; }
		public Func<object, object> Getter { get; }
		public Action<object, object> Setter { get; }
		public Func<object, object[], object> Invoker { get; }
		public IReadOnlyList<ParameterInfo> Parameters { get; }
		
		public DebugMemberInfo(
			string name,
			string displayName,
			DebugMemberKind kind,
			Type valueType,
			int order,
			string category,
			bool readOnly,
			Func<object, object> getter,
			Action<object, object> setter,
			Func<object, object[], object> invoker,
			IReadOnlyList<ParameterInfo> parameters)
		{
			Name = name;
			DisplayName = displayName;
			Kind = kind;
			ValueType = valueType;
			Order = order;
			Category = category;
			ReadOnly = readOnly;
			Getter = getter;
			Setter = setter;
			Invoker = invoker;
			Parameters = parameters;
		}
	}

	public sealed class DebugClassInfo
	{
		public Type Type { get; }
		public string DisplayName { get; }
		public int Order { get; }
		public string Category { get; }
		public List<DebugMemberInfo> Members { get; }
		public List<UnityEngine.Object> Instances { get; }
		
		public DebugClassInfo(Type type, string displayName, int order, string category, List<DebugMemberInfo> members)
		{
			Type = type;
			DisplayName = displayName;
			Order = order;
			Category = category;
			Members = members;
			Instances = new List<UnityEngine.Object>();
		}
	}
}
