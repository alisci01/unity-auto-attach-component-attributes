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

        public static GameObject GetGameObject(this SerializedProperty property)
        {
            return (property.serializedObject.targetObject as Component).gameObject;
        }

        public static Type GetComponentType(this SerializedProperty property)
        {
            return property.GetPropertyType().StringToComponentType();
        }

        private static FieldInfo s_CustomPropertyDrawerTypeField =
            typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic);

        private static FieldInfo s_CustomPropertyDrawerUseForChildrenField =
            typeof(CustomPropertyDrawer).GetField("m_UseForChildren",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool TargetsType(this CustomPropertyDrawer cpd, Type type)
        {
            var cpdType = (Type)s_CustomPropertyDrawerTypeField.GetValue(cpd);
            var useForChildren = (bool)s_CustomPropertyDrawerUseForChildrenField.GetValue(cpd);

            return useForChildren ? type.IsSubclassOf(cpdType) : cpdType == type;
        }

        // prefetch certain types
        private static Type[] s_AllTypes = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToArray();
        private static Type[] s_AllComponentTypes = s_AllTypes.Where(x => x.IsSubclassOf(typeof(Component))).ToArray();
        private static Type[] s_AllStaticTypes = s_AllTypes.Where(x => x.IsAbstract && x.IsSealed).ToArray();
        private static Type[] s_AllPropertyDrawers = s_AllTypes.Where(x => x.IsSubclassOf(typeof(PropertyDrawer))).ToArray();

        public static Type StringToComponentType(this string aClassName) => s_AllComponentTypes.First(x => x.Name == aClassName);
        public static Type StringToStaticType(this string aClassName) => s_AllStaticTypes.First(x => x.FullName == aClassName);
        
        private static Dictionary<string, PropertyDrawer> s_PropertyDrawers = new Dictionary<string, PropertyDrawer>();

        private static PropertyDrawer GetPropertyDrawerForProperty(SerializedProperty prop)
        {
            string key = prop.propertyPath + prop.serializedObject.targetObject.GetInstanceID();

            if (!s_PropertyDrawers.TryGetValue(key, out var drawer))
            {
                var propertyTypeStr = prop.GetPropertyType();
                var propertyType = s_AllTypes.First(x => x.Name == propertyTypeStr);

                try
                {
                    Type drawerType = s_AllPropertyDrawers.First(x =>
                        x.GetCustomAttributes<CustomPropertyDrawer>().Any(cpd => cpd.TargetsType(propertyType)));
                
                    drawer = (PropertyDrawer)Activator.CreateInstance(drawerType);
                }
                catch (InvalidOperationException e)
                {
                    drawer = null;
                }

                s_PropertyDrawers[key] = drawer;
            }

            return drawer;
        }

        public static void DefaultPropertyGUI(Rect pos, SerializedProperty prop, GUIContent label, bool includeChildren)
        {
            PropertyDrawer drawer = GetPropertyDrawerForProperty(prop);

            if (drawer != null)
            {
                drawer.OnGUI(pos, prop, label);
            }
            else
            {
                EditorGUI.PropertyField(pos, prop, label, includeChildren);
            }
        }

        public static float GetDefaultPropertyGUIHeight(SerializedProperty prop, GUIContent label)
        {
            PropertyDrawer drawer = GetPropertyDrawerForProperty(prop);

            if (drawer != null)
            {
                return drawer.GetPropertyHeight(prop, label);
            }
            else
            {
                return EditorGUI.GetPropertyHeight(prop, label, true);
            }
        }

        private static Dictionary<(Type, string), MethodInfo> s_FetchMethods =
            new Dictionary<(Type, string), MethodInfo>();

        private static MethodInfo GetCustomFetchMethod(SerializedProperty property, BaseCustomFetchAttribute attribute, string funcName, Type returnType, params Type[] parameterTypes)
        {
            int posOfLastPeriod = funcName.LastIndexOf('.');
            Type baseType = funcName.Substring(0, posOfLastPeriod).StringToStaticType();
            string localFuncName = funcName.Substring(posOfLastPeriod + 1);

            (Type, string) pair = (baseType, localFuncName);

            MethodInfo ret = null;

            // reflect the proper method if we don't have it cached already
            if (!s_FetchMethods.TryGetValue(pair, out ret))
            {
                var bindingFlags = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var method = baseType.GetMethod(localFuncName, bindingFlags);

                // make sure the method returns our property type
                if (method != null && method.ReturnType == returnType)
                {
                    var parameters = method.GetParameters();

                    bool failedParamCheck = parameters.Length != parameterTypes.Length;
                    for (int i = 0; !failedParamCheck && i < parameterTypes.Length; i++)
                    {
                        if (parameters[i].ParameterType != parameterTypes[i])
                        {
                            failedParamCheck = true;
                        }
                    }

                    if (!failedParamCheck)
                    {
                        ret = method;
                    }
                }
            }

            return ret;
        }
        
        public static MethodInfo GetFetchMethod(SerializedProperty baseProperty, BaseCustomFetchAttribute attribute)
        {
            return GetCustomFetchMethod(baseProperty, attribute, attribute.CustomFuncName, typeof(void), typeof(SerializedProperty), attribute.GetType());
        }

        public static MethodInfo GetFetchValidationMethod(SerializedProperty baseProperty, BaseCustomFetchAttribute attribute)
        {
            return GetCustomFetchMethod(baseProperty, attribute, attribute.CustomValidationFuncName, typeof(bool), typeof(SerializedProperty), attribute.GetType());
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
                AttachAttributesUtils.DefaultPropertyGUI(position, property, label, true);
                if (attachAttributeEnabled && ShouldUpdateProperty(property))
                {
                    UpdateProperty(property);
                }
            }

            EditorGUI.EndProperty();
        }

        /// Customize it for each attribute
        public virtual void UpdateProperty(SerializedProperty property)
        {
            // Do whatever
            // For example to get component 
            // property.objectReferenceValue = go.GetComponent(type);
        }

        /// Can be customized per attribute
        public virtual bool ShouldUpdateProperty(SerializedProperty property)
        {
            return property.objectReferenceValue == null;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return AttachAttributesUtils.GetDefaultPropertyGUIHeight(property, label);
        }
    }

    #region Attribute Editors

    /// GetComponent
    [CustomPropertyDrawer(typeof(GetComponentAttribute))]
    public class GetComponentAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property)
        {
            var type = property.GetComponentType();
            var go = property.GetGameObject();
            
            property.objectReferenceValue = go.GetComponent(type);
        }
    }

    /// GetComponentInChildren
    [CustomPropertyDrawer(typeof(GetComponentInChildrenAttribute))]
    public class GetComponentInChildrenAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property)
        {
            var type = property.GetComponentType();
            var go = property.GetGameObject();
            
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
        public override void UpdateProperty(SerializedProperty property)
        {
            var type = property.GetComponentType();
            var go = property.GetGameObject();
            
            property.objectReferenceValue = go.AddComponent(type);
        }
    }

    /// FindObjectOfType
    [CustomPropertyDrawer(typeof(FindObjectOfTypeAttribute))]
    public class FindObjectOfTypeAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property)
        {
            var type = property.GetComponentType();
            var go = property.GetGameObject();
            
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
        public override void UpdateProperty(SerializedProperty property)
        {
            var type = property.GetComponentType();
            var go = property.GetGameObject();
            
            if (go.transform.parent != null)
                property.objectReferenceValue = go.transform.parent.gameObject.GetComponent(type);
        }
    }

    /// CustomFetch
    [CustomPropertyDrawer(typeof(BaseCustomFetchAttribute), useForChildren: true)]
    public class CustomFetchAttributeEditor : AttachAttributePropertyDrawer
    {
        public override void UpdateProperty(SerializedProperty property)
        {
            BaseCustomFetchAttribute fetchAttribute = (BaseCustomFetchAttribute)attribute;
            var methodInfo = AttachAttributesUtils.GetFetchMethod(property, fetchAttribute);

            if (methodInfo == null)
            {
                EditorGUILayout.HelpBox($"Unable to find method \"{fetchAttribute.CustomFuncName}\"; ensure the method is static that returns nothing and takes in a \"{nameof(SerializedProperty)}\" for the first parameter and \"{fetchAttribute.GetType().Name}\" as the second parameter.", MessageType.Error);
            }
            else
            {
                methodInfo.Invoke(null, new object[] { property, fetchAttribute });
            }
        }

        public override bool ShouldUpdateProperty(SerializedProperty property)
        {
            BaseCustomFetchAttribute fetchAttribute = (BaseCustomFetchAttribute)attribute;
            if (string.IsNullOrEmpty(fetchAttribute.CustomValidationFuncName))
            {
                return base.ShouldUpdateProperty(property);
            }
            
            var methodInfo = AttachAttributesUtils.GetFetchValidationMethod(property, fetchAttribute);
            bool ret = false;

            if (methodInfo == null)
            {
                EditorGUILayout.HelpBox($"Unable to find method \"{fetchAttribute.CustomValidationFuncName}\"; ensure the method is static that returns a boolean and takes in a \"{nameof(SerializedProperty)}\" for the first parameter and \"{fetchAttribute.GetType().Name}\" as the second parameter.", MessageType.Error);
            }
            else
            {
                ret = (bool)methodInfo.Invoke(null, new object[] { property, fetchAttribute });
            }

            return ret;
        }
    }
    #endregion
}
