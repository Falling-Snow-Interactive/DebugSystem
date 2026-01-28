using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fsi.General.Icons;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Fsi.Debug
{
	public sealed class DebugWindow : EditorWindow
	{
		private const string UssPath = "Packages/com.fallingsnowinteractive.debug/Editor/DebugWindow.uss";
		private const float ClassListItemHeight = 28f;

		private readonly List<DebugClassInfo> classes = new();
		private readonly List<Action> refreshActions = new();
		
		// Elements
		private ListView classListView;
		private VisualElement detailContainer;
		private DebugClassInfo selectedClass;
		private Object selectedInstance;
		private int selectedInstanceIndex;

		[MenuItem("FSI/Debug Window")]
		public static void ShowWindow()
		{
			DebugRegistry.Refresh();
			DebugWindow window = GetWindow<DebugWindow>("Debugging");
			window.titleContent = new GUIContent("Debug Window");

			Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPaths.Debug_Grey);
			window.titleContent.image = icon;
			
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

		#region Create GUI
		
		public void CreateGUI()
		{
			StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
			if (styleSheet != null)
			{
				rootVisualElement.styleSheets.Add(styleSheet);
			}
			
			rootVisualElement.Clear();
			rootVisualElement.AddToClassList("window-root");

			// Build sidebar
			VisualElement sidebar = CreateSidebar();
			rootVisualElement.Add(sidebar);
			
			// Build details panel
			VisualElement details = CreateDetailsPanel();
			rootVisualElement.Add(details);

			RefreshClassList();
		}

		private VisualElement CreateSidebar()
		{
			// Build sidebar
			VisualElement root = new();
			root.AddToClassList("sidebar-panel");
			
			// Toolbar
			Toolbar toolbar = new();
			toolbar.AddToClassList("toolbar");
			root.Add(toolbar);
			
			Label toolbarLabel = new("Debug Classes");
			toolbarLabel.AddToClassList("toolbar-label");
			toolbar.Add(toolbarLabel);
			
			toolbar.Add(new ToolbarSpacer());
			
			ToolbarButton refreshButton = new(RefreshClassList) { text = "Refresh" };
			refreshButton.AddToClassList("toolbar-button");
			toolbar.Add(refreshButton);

			// Class List
			ListView listView = new();
			classListView = listView;
			listView.AddToClassList("class-list");

			// ListView uses virtualization; it needs to know the row height for correct viewport/scroll calculations.
			// Keep this in sync with the USS height (including padding) for .class-item / list rows.
			listView.fixedItemHeight = ClassListItemHeight;
			
			listView.selectionType = SelectionType.Single;
			listView.makeItem = () =>
			                    {
				                    Label label = new();
				                    label.AddToClassList("class-item");
				                    // Ensure the element itself matches the ListView's fixed height.
				                    label.style.height = ClassListItemHeight;
				                    return label;
			                    };
			listView.bindItem = (element, index) =>
			                    {
				                    if (element is Label label && index >= 0 && index < classes.Count)
				                    {
					                    label.text = classes[index].DisplayName;
				                    }
			                    };
			listView.selectionChanged += OnClassSelectionChanged;

			root.Add(listView);
			
			return root;
		}

		private VisualElement CreateDetailsPanel()
		{
			VisualElement root = new();
			root.AddToClassList("detail-panel");
			
			// Detail panel
			ScrollView detailScrollView = new();
			detailScrollView.AddToClassList("detail-scroll");
			root.Add(detailScrollView);

			detailContainer = new VisualElement();
			detailContainer.AddToClassList("detail-container");
			detailScrollView.Add(detailContainer);

			return root;
		}
		
		#endregion
		
		#region Registry

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
		
		#endregion
		
		#region Selection

		private void OnClassSelectionChanged(IEnumerable<object> selectedItems)
		{
			DebugClassInfo newSelection = selectedItems?.OfType<DebugClassInfo>().FirstOrDefault();
			if (newSelection == selectedClass)
			{
				return;
			}

			selectedClass = newSelection;
			selectedInstanceIndex = 0;
			selectedInstance = selectedClass?.GetInstances().FirstOrDefault();
			RebuildDetails();
		}
		
		#endregion
		
		#region Details

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
			header.AddToClassList("detail-header");
			detailContainer.Add(header);
			
			// Toolbar
			Toolbar toolbar = new();
			toolbar.AddToClassList("toolbar");
			detailContainer.Add(toolbar);

			AddInstanceSelector(toolbar);
			
			ToolbarSpacer spacer = new();
			spacer.AddToClassList("toolbar-spacer");
			toolbar.Add(spacer);
			
			ToolbarButton refreshButton = new(RefreshClassList) { text = "Refresh" };
			refreshButton.AddToClassList("toolbar-button");
			toolbar.Add(refreshButton);
			
			AddMemberDetails();
		}

		private void AddInstanceSelector(Toolbar toolbar)
		{
			if (selectedClass == null)
			{
				return;
			}

			List<Object> instances = selectedClass.GetInstances();
			if (instances.Count == 0)
			{
				Label emptyLabel = new("No instances found for this type.");
				emptyLabel.AddToClassList("empty-message");
				emptyLabel.AddToClassList("toolbar-label");
				toolbar.Add(emptyLabel);
				selectedInstance = null;
				return;
			}

			List<string> instanceLabels = instances.Select(instance => instance == null
				                                                           ? "<null>"
				                                                           : $"{instance.name} ({instance.GetInstanceID()})")
			                                       .ToList();

			selectedInstanceIndex = Mathf.Clamp(selectedInstanceIndex, 0, instanceLabels.Count - 1);
			selectedInstance = instances[selectedInstanceIndex];

			ToolbarMenu instanceMenu = new();
			instanceMenu.AddToClassList("toolbar-menu");
			instanceMenu.text = $"{selectedInstance.name} ({selectedInstance.GetInstanceID()})";
			foreach (string instanceLabel in instanceLabels)
			{
				instanceMenu.menu.AppendAction($"{instanceLabel}", menuAction =>
				                                                   {
					                                                   int newIndex = instanceLabels.IndexOf(menuAction.name);
					                                                   if (newIndex >= 0 && newIndex < instances.Count)
					                                                   {
						                                                   selectedInstanceIndex = newIndex;
						                                                   selectedInstance = instances[newIndex];
						                                                   instanceMenu.text = $"{selectedInstance.name} ({selectedInstance.GetInstanceID()})";
						                                                   RebuildDetails();
					                                                   }
				                                                   });
			}

			toolbar.Add(instanceMenu);
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
			                                                .OrderBy(info => !string.IsNullOrWhiteSpace(info.Category))
			                                                .ThenBy(info => info.Category)
			                                                .ThenBy(info => info.Kind)
			                                                .ThenBy(info => info.Order))
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
			inputRow.AddToClassList("method-input-group");

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
			row.Add(inputRow);
			
			Button button = new() { text = "Invoke" };
			button.AddToClassList("invoke-button");
			row.Add(button);

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

			if (valueType == typeof(long))
			{
				LongField field = new() { value = initialValue is long longValue ? longValue : 0L };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is long current ? current : 0L));
				return field;
			}

			if (valueType == typeof(float))
			{
				FloatField field = new() { value = initialValue is float floatValue ? floatValue : 0f };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is float current ? current : 0f));
				return field;
			}

			if (valueType == typeof(double))
			{
				DoubleField field = new() { value = initialValue is double doubleValue ? doubleValue : 0d };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is double current ? current : 0d));
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

			if (valueType == typeof(Vector4))
			{
				Vector4Field field = new() { value = initialValue is Vector4 vec ? vec : Vector4.zero };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Vector4 current ? current : Vector4.zero));
				return field;
			}

			if (valueType == typeof(Rect))
			{
				RectField field = new() { value = initialValue is Rect rect ? rect : new Rect() };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Rect current ? current : new Rect()));
				return field;
			}

			if (valueType == typeof(Bounds))
			{
				BoundsField field = new() { value = initialValue is Bounds bounds ? bounds : new Bounds() };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Bounds current ? current : new Bounds()));
				return field;
			}

			if (valueType == typeof(Color))
			{
				ColorField field = new() { value = initialValue is Color color ? color : Color.white };
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() => field.SetValueWithoutNotify(valueGetter?.Invoke() is Color current ? current : Color.white));
				return field;
			}

			if (typeof(Object).IsAssignableFrom(valueType))
			{
				ObjectField field = new()
				{
					objectType = valueType,
					value = initialValue as Object
				};
				field.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
				refreshActions.Add(() =>
				                   {
					                   if (valueGetter?.Invoke() is Object current)
					                   {
						                   field.SetValueWithoutNotify(current);
					                   }
					                   else
					                   {
						                   field.SetValueWithoutNotify(null);
					                   }
				                   });
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
		
		#endregion

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
