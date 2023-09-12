using System;
using UnityEngine;

namespace Nrjwolf.Tools.AttachAttributes
{
    [AttributeUsage(System.AttributeTargets.Field)] public class GetComponentAttribute : AttachPropertyAttribute { }

    [AttributeUsage(System.AttributeTargets.Field)]
    public class GetComponentInChildrenAttribute : AttachPropertyAttribute
    {
        public bool IncludeInactive { get; private set; }
        public string ChildName;

        public GetComponentInChildrenAttribute(bool includeInactive = false)
        {
            IncludeInactive = includeInactive;
        }

        public GetComponentInChildrenAttribute(string childName)
        {
            ChildName = childName;
        }
    }

    [AttributeUsage(System.AttributeTargets.Field)] public class AddComponentAttribute : AttachPropertyAttribute { }
    [AttributeUsage(System.AttributeTargets.Field)] public class FindObjectOfTypeAttribute : AttachPropertyAttribute { }
    [AttributeUsage(System.AttributeTargets.Field)] public class GetComponentInParent : AttachPropertyAttribute { }

    [AttributeUsage(System.AttributeTargets.Field)]
    public class CustomFetchAttribute : BaseCustomFetchAttribute
    {
        public CustomFetchAttribute(string funcName, string validationFuncName = null) : base(funcName, validationFuncName) { }
    }

    public abstract class BaseCustomFetchAttribute : AttachPropertyAttribute
    {
        public string CustomFuncName;
        public string CustomValidationFuncName;

        protected BaseCustomFetchAttribute(string funcName, string validationFuncName = null)
        {
            CustomFuncName = funcName;
        }
    }

    public class AttachPropertyAttribute : PropertyAttribute { }
}
