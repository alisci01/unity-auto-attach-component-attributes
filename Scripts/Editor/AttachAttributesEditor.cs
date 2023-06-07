using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Nrjwolf.Tools.AttachAttributes;
using UnityEditor;
using UnityEngine;

namespace Nrjwolf.Tools.Editor.AttachAttributes
{
    public static class AttachAttributesUtils
    {
        private const string k_ContextMenuItemLabel = "CONTEXT/Component/AttachAttributes";
        private const string k_ToolsMenuItemLabel = "Tools/Nrjwolf/AttachAttributes";

        private const string k_EditorPrefsAttachAttributesGlobal = "IsAttachAttributesActive";

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(k_EditorPrefsAttachAttributesGlobal, true);
            set
            {
                if (value) EditorPrefs.DeleteKey(k_EditorPrefsAttachAttributesGlobal);
                else EditorPrefs.SetBool(k_EditorPrefsAttachAttributesGlobal, value); // clear value if it's equals defaultValue
            }
        }

        [MenuItem(k_ContextMenuItemLabel)]
        [MenuItem(k_ToolsMenuItemLabel)]
        private static void ToggleAction()
        {
            IsEnabled = !IsEnabled;
        }

        [MenuItem(k_ContextMenuItemLabel, true)]
        [MenuItem(k_ToolsMenuItemLabel, true)]
        private static bool ToggleActionValidate()
        {
            Menu.SetChecked(k_ContextMenuItemLabel, IsEnabled);
            Menu.SetChecked(k_ToolsMenuItemLabel, IsEnabled);
            return true;
        }

        public static string GetPropertyType(this SerializedProperty property)
        {
            var type = property.type;
            var match = Regex.Match(type, @"PPtr<\$(.*?)>");
            if (match.Success)
                type = match.Groups[1].Value;
            return type;
        }

        // prefetch certain types
        private static Type[] s_AllTypes = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToArray();
        private static Type[] s_AllComponentTypes = s_AllTypes.Where(x => x.IsSubclassOf(typeof(Component))).ToArray();
        private static Type[] s_AllStaticTypes = s_AllTypes.Where(x => x.IsAbstract && x.IsSealed).ToArray();

        public static Type StringToComponentType(this string aClassName) => s_AllComponentTypes.First(x => x.Name == aClassName);
        public static Type StringToStaticType(this string aClassName) => s_AllStaticTypes.First(x => x.Name == aClassName);

        private static Dictionary<(Type, string), MethodInfo> s_FetchMethods =
            new Dictionary<(Type, string), MethodInfo>();
        
        public static MethodInfo GetFetchMethod(SerializedProperty baseProperty, string fetchFuncName)
        {
            MethodInfo ret = null;

            Type baseType = null;
            (Type, string) pair;
            bool usesStaticMethod = fetchFuncName.Contains(".");

            if (usesStaticMethod)
            {
                int posOfLastPeriod = fetchFuncName.LastIndexOf('.');
                baseType = fetchFuncName.Substring(0, posOfLastPeriod).StringToStaticType();
                fetchFuncName = fetchFuncName.Substring(posOfLastPeriod + 1);
            }
            else
            {
                baseType = baseProperty.serializedObject.targetObject.GetType();
            }

            pair = (baseType, fetchFuncName);
            
            if (!s_FetchMethods.TryGetValue(pair, out ret))
            {
                var bindingFlags = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.NonPublic;
                if (usesStaticMethod)
                {
                    bindingFlags |= BindingFlags.Static;
                }
                else
                {
                    bindingFlags |= BindingFlags.Instance;
                }
                
                var method = baseType.GetMethod(fetchFuncName, bindingFlags);
                var parameters = method?.GetParameters();

                if (method != null && method.ReturnType == typeof(UnityEngine.Object))
                {
                    var validStaticMethod = parameters.Length == 1 && parameters[0].ParameterType == typeof(UnityEngine.GameObject);
                    if (!usesStaticMethod || (usesStaticMethod && validStaticMethod))
                    {
                        ret = method;
                    }
                }
            }

            return ret;
        }
    }

    /// Base class for Attach Attribute
    public class AttachAttributePropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // turn off attribute if not active or in Play Mode (imitate as build will works)
            bool attachAttributeEnabled = AttachAttributesUtils.IsEnabled && !Application.isPlaying;
            using (new EditorGUI.DisabledScope(disabled: attachAttributeEnabled))
            {
                EditorGUI.PropertyField(position, property, label, true);
                if (attachAttributeEnabled && property.objectReferenceValue == null)
                {
                    var type = property.GetPropertyType().StringToComponentType();
                    var go = (property.serializedObject.targetObject as Component).gameObject;
                    UpdateProperty(property, go, type);
                }
            }

            EditorGUI.EndProperty();
        }

        /// Customize it for each attribute
        public virtual void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            // Do whatever
            // For example to get component 
            // property.objectReferenceValue = go.GetComponent(type);
        }
    }

    #region Attribute Editors

    /// GetComponent
    [CustomPropertyDrawer(typeof(GetComponentAttribute))]
    public class GetComponentAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            property.objectReferenceValue = go.GetComponent(type);
        }
    }

    /// GetComponentInChildren
    [CustomPropertyDrawer(typeof(GetComponentInChildrenAttribute))]
    public class GetComponentInChildrenAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            GetComponentInChildrenAttribute labelAttribute = (GetComponentInChildrenAttribute)attribute;
            if (labelAttribute.ChildName == null)
            {
                property.objectReferenceValue = go.GetComponentInChildren(type, labelAttribute.IncludeInactive);
            }
            else
            {
                var child = go.transform.Find(labelAttribute.ChildName);
                if (child != null)
                {
                    property.objectReferenceValue = child.GetComponent(type);
                }
            }
        }
    }

    /// AddComponent
    [CustomPropertyDrawer(typeof(AddComponentAttribute))]
    public class AddComponentAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            property.objectReferenceValue = go.AddComponent(type);
        }
    }

    /// FindObjectOfType
    [CustomPropertyDrawer(typeof(FindObjectOfTypeAttribute))]
    public class FindObjectOfTypeAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            property.objectReferenceValue = FindObjectsOfTypeByName(property.GetPropertyType());
        }

        public UnityEngine.Object FindObjectsOfTypeByName(string aClassName)
        {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var types = assemblies[i].GetTypes();
                for (int n = 0; n < types.Length; n++)
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(types[n]) && aClassName == types[n].Name)
                        return UnityEngine.Object.FindObjectOfType(types[n]);
                }
            }
            return new UnityEngine.Object();
        }
    }

    /// GetComponentInParent
    [CustomPropertyDrawer(typeof(GetComponentInParent))]
    public class GetComponentInParentAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            if (go.transform.parent != null)
                property.objectReferenceValue = go.transform.parent.gameObject.GetComponent(type);
        }
    }

    /// CustomFetch
    [CustomPropertyDrawer(typeof(CustomFetchAttribute))]
    public class CustomFetchAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property, GameObject go, Type type)
        {
            CustomFetchAttribute fetchAttribute = (CustomFetchAttribute)attribute;
            var methodInfo = AttachAttributesUtils.GetFetchMethod(property, fetchAttribute.CustomFuncName);

            if (methodInfo == null)
            {
                EditorGUILayout.HelpBox($"Unable to find method \"{fetchAttribute.CustomFuncName}\"; ensure the method returns UnityEngine.Object and no parameters if it is not static. If the method is static, it must take a single GameObject parameter as well.", MessageType.Error);
            }
            else
            {
                object instance = methodInfo.IsStatic ? null : property.serializedObject.targetObject;
                property.objectReferenceValue = (UnityEngine.Object)methodInfo.Invoke(instance, methodInfo.IsStatic ? new object[] { ((Component)property.serializedObject.targetObject).gameObject } : null);
            }
        }
    }
    #endregion
}
