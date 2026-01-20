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
			rootVisualElement.style.flexDirection = FlexDirection.Row;

			VisualElement sidebar = new()
			                        {
				                        style =
				                        {
					                        width = SidebarWidth,
					                        flexShrink = 0,
					                        flexGrow = 0,
					                        paddingLeft = 6,
					                        paddingRight = 6,
					                        paddingTop = 6,
				                        },
			                        };

			Label sidebarLabel = new("Debug Classes")
			                     {
				                     style =
				                     {
					                     unityFontStyleAndWeight = FontStyle.Bold,
				                     },
			                     };
			sidebar.Add(sidebarLabel);

			classListView = new ListView
			                {
				                selectionType = SelectionType.Single,
				                style =
				                {
					                flexGrow = 1,
					                marginTop = 6,
				                },
				                makeItem = () => new Label(),
				                bindItem = (element, index) =>
				                           {
					                           if (element is Label label && index >= 0 && index < classes.Count)
					                           {
						                           label.text = classes[index].DisplayName;
					                           }
				                           },
			                };

			classListView.selectionChanged += OnClassSelectionChanged;
			sidebar.Add(classListView);
			rootVisualElement.Add(sidebar);

			detailScrollView = new ScrollView
			                   {
				                   style =
				                   {
					                   flexGrow = 1,
					                   paddingLeft = 10,
					                   paddingRight = 10,
					                   paddingTop = 6,
				                   },
			                   };

			detailContainer = new VisualElement
			                  {
				                  style =
				                  {
					                  flexGrow = 1,
				                  },
			                  };
			detailScrollView.Add(detailContainer);

			rootVisualElement.Add(detailScrollView);

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
				detailContainer.Add(new Label("Select a debug class to inspect."));
				return;
			}

			Label header = new(selectedClass.DisplayName)
			               {
				               style =
				               {
					               unityFontStyleAndWeight = FontStyle.Bold,
					               fontSize = 14,
					               marginBottom = 6,
				               },
			               };
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
				detailContainer.Add(new Label("No instances found for this type."));
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

			VisualElement instanceRow = new()
			                            {
				                            style =
				                            {
					                            flexDirection = FlexDirection.Row,
					                            alignItems = Align.Center,
					                            marginBottom = 8,
				                            },
			                            };

			Label label = new("Instance")
			              {
				              style =
				              {
					              minWidth = 80,
				              },
			              };
			instanceRow.Add(label);

			PopupField<string> popup = new(instanceLabels, selectedInstanceIndex)
			                           {
				                           style =
				                           {
					                           flexGrow = 1,
				                           },
			                           };
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
				detailContainer.Add(new Label("No debug members available."));
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
						Label categoryLabel = new(currentCategory)
						                      {
							                      style =
							                      {
								                      unityFontStyleAndWeight = FontStyle.Bold,
								                      marginTop = 6,
								                      marginBottom = 4,
							                      },
						                      };
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
			VisualElement row = new()
			                    {
				                    style =
				                    {
					                    flexDirection = FlexDirection.Row,
					                    alignItems = Align.Center,
					                    marginBottom = 4,
				                    },
			                    };

			Label nameLabel = new(member.DisplayName)
			                  {
				                  style =
				                  {
					                  minWidth = 160,
					                  flexShrink = 0,
				                  },
			                  };
			row.Add(nameLabel);

			VisualElement valueContainer = new()
			                               {
				                               style =
				                               {
					                               flexGrow = 1,
				                               },
			                               };

			if (selectedInstance == null || member.Getter == null)
			{
				valueContainer.Add(new Label("N/A"));
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

			valueContainer.Add(field);
			row.Add(valueContainer);
			return row;
		}

		private VisualElement BuildMethodRow(DebugMemberInfo member)
		{
			VisualElement row = new()
			                    {
				                    style =
				                    {
					                    flexDirection = FlexDirection.Column,
					                    marginBottom = 8,
				                    },
			                    };

			Label nameLabel = new(member.DisplayName)
			                  {
				                  style =
				                  {
					                  unityFontStyleAndWeight = FontStyle.Bold,
				                  },
			                  };
			row.Add(nameLabel);

			VisualElement inputRow = new()
			                         {
				                         style =
				                         {
					                         flexDirection = FlexDirection.Row,
					                         alignItems = Align.Center,
					                         marginTop = 4,
				                         },
			                         };

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

					parameterField.style.marginRight = 6;
					inputRow.Add(parameterField);
				}
			}

			Label resultLabel = null;
			Button button = new()
			                {
				                text = "Invoke",
				                style =
				                {
					                minWidth = 80,
				                },
			                };
			inputRow.Add(button);
			row.Add(inputRow);

			if (member.ValueType != typeof(void))
			{
				resultLabel = new Label("Result: -")
				              {
					              style =
					              {
						              marginLeft = 6,
					              },
				              };
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
