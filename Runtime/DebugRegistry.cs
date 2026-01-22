using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Fsi.Debug.Attributes;
using UnityEditor;
using UnityEngine;

namespace Fsi.Debug
{
	public static class DebugRegistry
	{
		private static readonly object SyncRoot = new object();
		private static readonly List<DebugClassInfo> CachedClasses = new List<DebugClassInfo>();
		private static bool initialized;

		public static IReadOnlyList<DebugClassInfo> Classes
		{
			get
			{
				EnsureInitialized();
				return CachedClasses;
			}
		}

		public static event Action RegistryUpdated;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
		private static void RuntimeInitialize()
		{
			Refresh();
		}

		#if UNITY_EDITOR
		[InitializeOnLoadMethod]
		private static void EditorInitialize()
		{
			AssemblyReloadEvents.afterAssemblyReload += Refresh;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			Refresh();
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			if (change is PlayModeStateChange.EnteredPlayMode 
			              or PlayModeStateChange.ExitingPlayMode 
			              or PlayModeStateChange.EnteredEditMode)
			{
				Refresh();
			}
		}
		#endif

		public static void Refresh()
		{
			lock (SyncRoot)
			{
				CachedClasses.Clear();
				BuildCache();
				initialized = true;
			}

			RegistryUpdated?.Invoke();
		}

		public static void Invalidate()
		{
			lock (SyncRoot)
			{
				initialized = false;
				CachedClasses.Clear();
			}
		}

		private static void EnsureInitialized()
		{
			if (initialized)
			{
				return;
			}

			Refresh();
		}

		private static void BuildCache()
		{
			foreach (Type type in GetDebuggableTypes())
			{
				DebugClassAttribute debugClassAttribute = type.GetCustomAttribute<DebugClassAttribute>();
				string displayName = string.IsNullOrWhiteSpace(debugClassAttribute.DisplayName)
					                     ? type.Name
					                     : debugClassAttribute.DisplayName;

				List<DebugMemberInfo> members = new();
				PopulateMembers(type, members);

				DebugClassInfo classInfo = new(type,
				                               displayName,
				                               debugClassAttribute.Order,
				                               debugClassAttribute.Category,
				                               members);

				CachedClasses.Add(classInfo);
			}
		}

		private static IEnumerable<Type> GetDebuggableTypes()
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (var assembly in assemblies)
			{
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException ex)
				{
					types = ex.Types.Where(type => type != null).ToArray();
				}

				foreach (Type type in types)
				{
					if (type == null || type.IsAbstract)
					{
						continue;
					}

					if (type.GetCustomAttribute<DebugClassAttribute>() == null)
					{
						continue;
					}

					yield return type;
				}
			}
		}

		[SuppressMessage("ReSharper", "LoopCanBeConvertedToQuery")]
		private static void PopulateMembers(Type type, List<DebugMemberInfo> members)
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

			foreach (PropertyInfo property in type.GetProperties(flags))
			{
				DebugPropertyAttribute attribute = property.GetCustomAttribute<DebugPropertyAttribute>();
				if (attribute == null)
				{
					continue;
				}

				Func<object, object> getter = property.CanRead ? BuildGetter(property) : null;
				Action<object, object> setter = !attribute.ReadOnly && property.CanWrite ? BuildSetter(property) : null;
				members.Add(new DebugMemberInfo(property.Name,
				                                ResolveDisplayName(property.Name, attribute.DisplayName),
				                                DebugMemberKind.Property,
				                                property.PropertyType,
				                                attribute.Order,
				                                attribute.Category,
				                                attribute.ReadOnly || !property.CanWrite,
				                                getter,
				                                setter,
				                                null,
				                                null));
			}

			foreach (FieldInfo field in type.GetFields(flags))
			{
				DebugPropertyAttribute attribute = field.GetCustomAttribute<DebugPropertyAttribute>();
				if (attribute == null)
				{
					continue;
				}

				Func<object, object> getter = BuildGetter(field);
				Action<object, object> setter = !attribute.ReadOnly && !field.IsInitOnly ? BuildSetter(field) : null;
				members.Add(new DebugMemberInfo(
				                                field.Name,
				                                ResolveDisplayName(field.Name, attribute.DisplayName),
				                                DebugMemberKind.Field,
				                                field.FieldType,
				                                attribute.Order,
				                                attribute.Category,
				                                attribute.ReadOnly || field.IsInitOnly,
				                                getter,
				                                setter,
				                                null,
				                                null));
			}

			foreach (MethodInfo method in type.GetMethods(flags))
			{
				DebugMethodAttribute attribute = method.GetCustomAttribute<DebugMethodAttribute>();
				if (attribute == null)
				{
					continue;
				}

				Func<object, object[], object> invoker = BuildInvoker(method);
				ParameterInfo[] parameters = method.GetParameters();
				members.Add(new DebugMemberInfo(
				                                method.Name,
				                                ResolveDisplayName(method.Name, attribute.DisplayName),
				                                DebugMemberKind.Method,
				                                method.ReturnType,
				                                attribute.Order,
				                                attribute.Category,
				                                readOnly: true,
				                                getter: null,
				                                setter: null,
				                                invoker: invoker,
				                                parameters: parameters));
			}
		}

		private static string ResolveDisplayName(string defaultName, string displayName)
		{
			return string.IsNullOrWhiteSpace(displayName) ? defaultName : displayName;
		}

		private static Func<object, object> BuildGetter(PropertyInfo property)
		{
			ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
			if (property.DeclaringType != null)
			{
				UnaryExpression castInstance = Expression.Convert(instance, property.DeclaringType);
				MemberExpression access = Expression.Property(castInstance, property);
				UnaryExpression castResult = Expression.Convert(access, typeof(object));
				return Expression.Lambda<Func<object, object>>(castResult, instance).Compile();
			}

			UnityEngine.Debug.LogError("No declaring type.");
			return null;
		}

		private static Func<object, object> BuildGetter(FieldInfo field)
		{
			ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
			if (field.DeclaringType != null)
			{
				UnaryExpression castInstance = Expression.Convert(instance, field.DeclaringType);
				MemberExpression access = Expression.Field(castInstance, field);
				UnaryExpression castResult = Expression.Convert(access, typeof(object));
				return Expression.Lambda<Func<object, object>>(castResult, instance).Compile();
			}
			
			UnityEngine.Debug.LogError("No declaring type.");
			return null;
		}

		private static Action<object, object> BuildSetter(PropertyInfo property)
		{
			ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
			ParameterExpression value = Expression.Parameter(typeof(object), "value");
			if (property.DeclaringType != null)
			{
				UnaryExpression castInstance = Expression.Convert(instance, property.DeclaringType);
				UnaryExpression castValue = Expression.Convert(value, property.PropertyType);
				MemberExpression access = Expression.Property(castInstance, property);
				BinaryExpression assign = Expression.Assign(access, castValue);
				return Expression.Lambda<Action<object, object>>(assign, instance, value).Compile();
			}
			
			UnityEngine.Debug.LogError("No declaring type.");
			return null;
		}

		private static Action<object, object> BuildSetter(FieldInfo field)
		{
			ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
			ParameterExpression value = Expression.Parameter(typeof(object), "value");
			if (field.DeclaringType != null)
			{
				UnaryExpression castInstance = Expression.Convert(instance, field.DeclaringType);
				UnaryExpression castValue = Expression.Convert(value, field.FieldType);
				MemberExpression access = Expression.Field(castInstance, field);
				BinaryExpression assign = Expression.Assign(access, castValue);
				return Expression.Lambda<Action<object, object>>(assign, instance, value).Compile();
			}
			
			UnityEngine.Debug.LogError("No declaring type.");
			return null;
		}

		private static Func<object, object[], object> BuildInvoker(MethodInfo method)
		{
			ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
			ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
			if (method.DeclaringType != null)
			{
				UnaryExpression castInstance = Expression.Convert(instance, method.DeclaringType);
				ParameterInfo[] parameters = method.GetParameters();
				Expression[] arguments = new Expression[parameters.Length];
				for (int i = 0; i < parameters.Length; i++)
				{
					ParameterInfo parameter = parameters[i];
					BinaryExpression argumentAccess = Expression.ArrayIndex(args, Expression.Constant(i));
					UnaryExpression castArgument = Expression.Convert(argumentAccess, parameter.ParameterType);
					arguments[i] = castArgument;
				}

				MethodCallExpression call = Expression.Call(castInstance, method, arguments);
				Expression body = method.ReturnType == typeof(void)
					                  ? Expression.Block(call, Expression.Constant(null))
					                  : Expression.Convert(call, typeof(object));

				return Expression.Lambda<Func<object, object[], object>>(body, instance, args).Compile();
			}
			
			UnityEngine.Debug.LogError("No declaring type.");
			return null;
		}
	}
}
