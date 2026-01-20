using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fantazee.Debugging;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Fsi.Debug
{
	public sealed class DebugWindow : EditorWindow
	{
		private const float SidebarWidth = 260f;
		private const string UxmlPath = "Packages/com.fallingsnowinteractive.debug/Editor/DebugWindow.uxml";
		private const string UssPath = "Packages/com.fallingsnowinteractive.debug/Editor/DebugWindow.uss";

		private readonly List<DebugClassInfo> classes = new();
		private readonly List<Action> refreshActions = new();
		private ListView classListView;
		private ScrollView detailScrollView;
		private VisualElement detailContainer;
		private DebugClassInfo selectedClass;
		private Object selectedInstance;
		private int selectedInstanceIndex;

		[MenuItem("FSI/Debug Window")]
		public static void ShowWindow()
		{
			DebugRegistry.Refresh();
			DebugWindow window = GetWindow<DebugWindow>();
			window.titleContent = new GUIContent("Debug Window");
			window.Show();
		}

		private void OnEnable()
		{
			DebugRegistry.RegistryUpdated += OnRegistryUpdated;
			EditorApplication.update += OnEditorUpdate;
			RefreshClassList();
		}

		private void OnDisable()
		{
			DebugRegistry.RegistryUpdated -= OnRegistryUpdated;
			EditorApplication.update -= OnEditorUpdate;
		}

		public void CreateGUI()
		{
			rootVisualElement.Clear();
			rootVisualElement.AddToClassList("debug-window");

			VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			if (visualTree != null)
			{
				visualTree.CloneTree(rootVisualElement);
			}

			StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
			if (styleSheet != null)
			{
				rootVisualElement.styleSheets.Add(styleSheet);
			}

			VisualElement sidebar = rootVisualElement.Q<VisualElement>("sidebar");
			classListView = rootVisualElement.Q<ListView>("class-list");
			detailScrollView = rootVisualElement.Q<ScrollView>("detail-scroll");
			detailContainer = rootVisualElement.Q<VisualElement>("detail-container");

			if (sidebar != null)
			{
				sidebar.style.width = SidebarWidth;
			}

			if (classListView != null)
			{
				classListView.selectionType = SelectionType.Single;
				classListView.makeItem = () =>
				{
					Label label = new();
					label.AddToClassList("debug-class-item");
					return label;
				};
				classListView.bindItem = (element, index) =>
				{
					if (element is Label label && index >= 0 && index < classes.Count)
					{
						label.text = classes[index].DisplayName;
					}
				};
				classListView.selectionChanged += OnClassSelectionChanged;
			}

			RefreshClassList();
		}

		private void OnRegistryUpdated()
		{
			RefreshClassList();
		}

		private void RefreshClassList()
		{
			classes.Clear();
			classes.AddRange(DebugRegistry.Classes
			                              .OrderBy(info => string.IsNullOrWhiteSpace(info.Category))
			                              .ThenBy(info => info.Category)
			                              .ThenBy(info => info.Order)
			                              .ThenBy(info => info.DisplayName));

			if (classListView != null)
			{
				classListView.itemsSource = classes;
				classListView.Rebuild();
			}

			if (selectedClass != null)
			{
				int index = classes.FindIndex(info => info.Type == selectedClass.Type);
				if (index >= 0)
				{
					classListView?.SetSelection(index);
					return;
				}
			}

			selectedClass = null;
			selectedInstance = null;
			selectedInstanceIndex = 0;
			RebuildDetails();
		}

		private void OnClassSelectionChanged(IEnumerable<object> selectedItems)
		{
			DebugClassInfo newSelection = selectedItems?.OfType<DebugClassInfo>().FirstOrDefault();
			if (newSelection == selectedClass)
			{
				return;
			}

			selectedClass = newSelection;
			selectedInstanceIndex = 0;
			selectedInstance = selectedClass?.Instances.FirstOrDefault();
			RebuildDetails();
		}

		private void RebuildDetails()
		{
			if (detailContainer == null)
			{
				return;
			}

			detailContainer.Clear();
			refreshActions.Clear();

			if (selectedClass == null)
			{
				Label emptyLabel = new("Select a debug class to inspect.");
				emptyLabel.AddToClassList("empty-message");
				detailContainer.Add(emptyLabel);
				return;
			}

			Label header = new(selectedClass.DisplayName);
			header.AddToClassList("debug-header");
			detailContainer.Add(header);

			AddInstanceSelector();
			AddMemberDetails();
		}

		private void AddInstanceSelector()
		{
			if (selectedClass == null)
			{
				return;
			}

			if (selectedClass.Instances.Count == 0)
			{
				Label emptyLabel = new("No instances found for this type.");
				emptyLabel.AddToClassList("empty-message");
				detailContainer.Add(emptyLabel);
				selectedInstance = null;
				return;
			}

			List<string> instanceLabels = selectedClass.Instances
			                                           .Select(instance => instance == null
				                                                               ? "<null>"
				                                                               : $"{instance.name} ({instance.GetInstanceID()})")
			                                           .ToList();

			selectedInstanceIndex = Mathf.Clamp(selectedInstanceIndex, 0, instanceLabels.Count - 1);
			selectedInstance = selectedClass.Instances[selectedInstanceIndex];

			VisualElement instanceRow = new();
			instanceRow.AddToClassList("instance-row");

			Label label = new("Instance");
			label.AddToClassList("instance-label");
			instanceRow.Add(label);

			PopupField<string> popup = new(instanceLabels, selectedInstanceIndex);
			popup.AddToClassList("instance-popup");
			popup.RegisterValueChangedCallback(evt =>
			                                   {
				                                   int newIndex = instanceLabels.IndexOf(evt.newValue);
				                                   if (newIndex >= 0 && newIndex < selectedClass.Instances.Count)
				                                   {
					                                   selectedInstanceIndex = newIndex;
					                                   selectedInstance = selectedClass.Instances[newIndex];
					                                   RebuildDetails();
				                                   }
			                                   });

			instanceRow.Add(popup);
			detailContainer.Add(instanceRow);
		}

		private void AddMemberDetails()
		{
			if (selectedClass == null)
			{
				return;
			}

			if (selectedClass.Members.Count == 0)
			{
				Label emptyLabel = new("No debug members available.");
				emptyLabel.AddToClassList("empty-message");
				detailContainer.Add(emptyLabel);
				return;
			}

			string currentCategory = null;
			foreach (DebugMemberInfo member in selectedClass.Members
			                                                .OrderBy(info => string.IsNullOrWhiteSpace(info.Category))
			                                                .ThenBy(info => info.Category)
			                                                .ThenBy(info => info.Order)
			                                                .ThenBy(info => info.DisplayName))
			{
				if (member.Category != currentCategory)
				{
					currentCategory = member.Category;
					if (!string.IsNullOrWhiteSpace(currentCategory))
					{
						Label categoryLabel = new(currentCategory);
						categoryLabel.AddToClassList("category-label");
						detailContainer.Add(categoryLabel);
					}
				}

				VisualElement memberRow = member.Kind == DebugMemberKind.Method
					                          ? BuildMethodRow(member)
					                          : BuildValueRow(member);

				detailContainer.Add(memberRow);
			}
		}

		private VisualElement BuildValueRow(DebugMemberInfo member)
		{
			VisualElement row = new();
			row.AddToClassList("member-row");
			row.AddToClassList("member-row-value");

			Label nameLabel = new(member.DisplayName);
			nameLabel.AddToClassList("member-name");
			row.Add(nameLabel);

			VisualElement valueContainer = new();
			valueContainer.AddToClassList("member-value");

			if (selectedInstance == null || member.Getter == null)
			{
				Label notAvailable = new("N/A");
				notAvailable.AddToClassList("value-field");
				valueContainer.Add(notAvailable);
				row.Add(valueContainer);
				return row;
			}

			object initialValue = member.Getter(selectedInstance);
			bool readOnly = member.ReadOnly || member.Setter == null;
			VisualElement field = CreateValueField(member.ValueType,
			                                       initialValue,
			                                       readOnly,
			                                       newValue => member.Setter?.Invoke(selectedInstance, newValue),
			                                       () => member.Getter(selectedInstance));

			field.AddToClassList("value-field");
			valueContainer.Add(field);
			row.Add(valueContainer);
			return row;
		}

		private VisualElement BuildMethodRow(DebugMemberInfo member)
		{
			VisualElement row = new();
			row.AddToClassList("member-row");
			row.AddToClassList("member-row-method");

			Label nameLabel = new(member.DisplayName);
			nameLabel.AddToClassList("method-name");
			row.Add(nameLabel);

			VisualElement inputRow = new();
			inputRow.AddToClassList("method-input-row");

			object[] parameterValues = member.Parameters?.Select(GetDefaultValue).ToArray() ?? Array.Empty<object>();

			if (member.Parameters != null)
			{
				for (int i = 0; i < member.Parameters.Count; i++)
				{
					ParameterInfo parameter = member.Parameters[i];
					int parameterIndex = i;
					VisualElement parameterField = CreateValueField(parameter.ParameterType,
					                                                parameterValues[i],
					                                                readOnly: false,
					                                                newValue => parameterValues[parameterIndex] = newValue,
					                                                () => parameterValues[parameterIndex]);

					parameterField.AddToClassList("parameter-field");
					inputRow.Add(parameterField);
				}
			}

			Label resultLabel = null;
			Button button = new() { text = "Invoke" };
			button.AddToClassList("invoke-button");
			inputRow.Add(button);
			row.Add(inputRow);

			if (member.ValueType != typeof(void))
			{
				resultLabel = new Label("Result: -");
				resultLabel.AddToClassList("method-result");
				row.Add(resultLabel);
			}

			button.clicked += () => InvokeMethod(member, parameterValues, resultLabel);

			if (selectedInstance == null)
			{
				row.SetEnabled(false);
			}

			return row;
		}

		private void InvokeMethod(DebugMemberInfo member, object[] parameterValues, Label resultLabel)
		{
			if (selectedInstance == null)
			{
				return;
			}

			try
			{
				object result = member.Invoker?.Invoke(selectedInstance, parameterValues);
				if (resultLabel != null)
				{
					resultLabel.text = $"Result: {FormatValue(result)}";
				}
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogException(exception);
			}
		}

		private VisualElement CreateValueField(
			Type valueType,
			object initialValue,
			bool readOnly,
			Action<object> onValueChanged,
			Func<object> valueGetter)
		{
			if (readOnly)
			{
				Label label = new(FormatValue(initialValue));
				refreshActions.Add(() => label.text = FormatValue(valueGetter?.Invoke()));
				return label;
			}

			if (valueType == typeof(bool))
			{
				Toggle field = new() { value = initialValue is true };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is true));
				return field;
			}

			if (valueType == typeof(int))
			{
				IntegerField field = new() { value = initialValue is int intValue ? intValue : 0 };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is int current ? current : 0));
				return field;
			}

			if (valueType == typeof(float))
			{
				FloatField field = new() { value = initialValue is float floatValue ? floatValue : 0f };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is float current ? current : 0f));
				return field;
			}

			if (valueType == typeof(string))
			{
				TextField field = new() { value = initialValue?.ToString() ?? string.Empty };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke()?.ToString() ?? string.Empty));
				return field;
			}

			if (valueType.IsEnum)
			{
				Enum enumValue = initialValue as Enum ?? (Enum)Enum.GetValues(valueType).GetValue(0);
				EnumField field = new(enumValue);
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() =>
				                   {
					                   if (valueGetter?.Invoke() is Enum current)
					                   {
						                   field.SetValueWithoutNotify(current);
					                   }
				                   });
				return field;
			}

			if (valueType == typeof(Vector2))
			{
				Vector2Field field = new() { value = initialValue is Vector2 vec ? vec : Vector2.zero };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Vector2 current ? current : Vector2.zero));
				return field;
			}

			if (valueType == typeof(Vector3))
			{
				Vector3Field field = new() { value = initialValue is Vector3 vec ? vec : Vector3.zero };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Vector3 current ? current : Vector3.zero));
				return field;
			}

			if (valueType == typeof(Color))
			{
				ColorField field = new() { value = initialValue is Color color ? color : Color.white };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Color current ? current : Color.white));
				return field;
			}

			Label unsupported = new($"Unsupported: {valueType.Name}");
			return unsupported;
		}

		private static object GetDefaultValue(ParameterInfo parameter)
		{
			if (parameter.HasDefaultValue)
			{
				return parameter.DefaultValue;
			}

			Type parameterType = parameter.ParameterType;
			return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
		}

		private static string FormatValue(object value)
		{
			return value == null ? "-" : value.ToString();
		}

		private void OnEditorUpdate()
		{
			if (!EditorApplication.isPlaying)
			{
				return;
			}

			foreach (Action refreshAction in refreshActions)
			{
				refreshAction?.Invoke();
			}
		}
	}
}
