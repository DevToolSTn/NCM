﻿using System;
using System.Linq;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ColorManager.ICC;
using ColorManager.Conversion;
using ColorManager.ICC.Conversion;

namespace ColorManager
{
    /// <summary>
    /// Provides methods to convert from one color to another
    /// </summary>
    public unsafe class ColorConverter : IDisposable
    {
        #region Static Content

        /// <summary>
        /// States if the <see cref="ColorConverter"/> is initiated
        /// </summary>
        public static bool IsInitiated
        {
            get { return _IsInitiated; }
        }
        private static bool _IsInitiated = false;

        /// <summary>
        /// All available conversion paths that are used to convert from one color to another
        /// </summary>
        public static ConversionPath[] ConversionPaths
        {
            get { return _ConversionPaths.ToArray(); }
        }
        /// <summary>
        /// Field for the <see cref="ConversionPaths"/> property
        /// </summary>
        protected static List<ConversionPath> _ConversionPaths = new List<ConversionPath>();

        /// <summary>
        /// All available chromatic adaption methods
        /// </summary>
        public static ChromaticAdaption[] ChromaticAdaptions
        {
            get { return _ChromaticAdaptions.ToArray(); }
        }
        /// <summary>
        /// Field for the <see cref="ChromaticAdaptions"/> property
        /// </summary>
        protected static List<ChromaticAdaption> _ChromaticAdaptions = new List<ChromaticAdaption>();

        /// <summary>
        /// Initiates the <see cref="ColorConverter"/> class
        /// <para>If not called before the first <see cref="ColorConverter"/> instance,
        /// it will be called by the <see cref="ColorConverter"/> constructor</para>
        /// </summary>
        public static void Init()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type[] types;
            Type pathTp = typeof(ConversionPath);
            Type caTp = typeof(ChromaticAdaption);

            foreach (var assembly in assemblies)
            {
                types = assembly.GetTypes();

                var paths = from t in types
                            where t.IsSubclassOf(pathTp)
                            && t.GetConstructor(Type.EmptyTypes) != null
                            select Activator.CreateInstance(t) as ConversionPath;
                _ConversionPaths.AddRange(paths);

                var cas = from t in types
                          where t.IsSubclassOf(caTp)
                          && t.GetConstructor(Type.EmptyTypes) != null
                          select Activator.CreateInstance(t) as ChromaticAdaption;
                _ChromaticAdaptions.AddRange(cas);
            }
            _IsInitiated = true;
        }

        /// <summary>
        /// Adds a conversion path to the <see cref="ConversionPaths"/> list
        /// </summary>
        /// <param name="path">The conversion path to add</param>
        public static void AddConversionPath(ConversionPath path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!_ConversionPaths.Contains(path)) _ConversionPaths.Add(path);
        }

        /// <summary>
        /// Removes a conversion path from the <see cref="ConversionPaths"/> list
        /// </summary>
        /// <param name="path">The conversion path to remove</param>
        /// <returns>True if the conversion path was found and removed, false otherwise</returns>
        public static bool RemoveConversionPath(ConversionPath path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return _ConversionPaths.Remove(path);
        }

        /// <summary>
        /// Adds a conversion path to the <see cref="ConversionPaths"/> list
        /// </summary>
        /// <param name="ca">The chromatic adaption method to add</param>
        public static void AddChromaticAdaption(ChromaticAdaption ca)
        {
            if (ca == null) throw new ArgumentNullException(nameof(ca));
            if (!_ChromaticAdaptions.Contains(ca)) _ChromaticAdaptions.Add(ca);
        }

        /// <summary>
        /// Removes a conversion path from the <see cref="ConversionPaths"/> list
        /// </summary>
        /// <param name="ca">The chromatic adaption method to remove</param>
        /// <returns>True if the chromatic adaption method was found and removed, false otherwise</returns>
        public static bool RemoveChromaticAdaption(ChromaticAdaption ca)
        {
            if (ca == null) throw new ArgumentNullException(nameof(ca));
            return _ChromaticAdaptions.Remove(ca);
        }

        #endregion

        #region Variables

        /// <summary>
        /// The method used for the conversion
        /// </summary>
        protected ConversionDelegate ConversionMethod;
        /// <summary>
        /// The data used for the conversion
        /// </summary>
        protected ConversionData Data;
        /// <summary>
        /// States if this instance is disposed or not
        /// </summary>
        protected bool IsDisposed;

        /// <summary>
        /// The input color
        /// </summary>
        protected readonly Color InColor;
        /// <summary>
        /// The output color
        /// </summary>
        protected readonly Color OutColor;
        /// <summary>
        /// The pointer to the input color values
        /// </summary>
        protected readonly double* InValues;
        /// <summary>
        /// The pointer to the output color values
        /// </summary>
        protected readonly double* OutValues;

        /// <summary>
        /// The <see cref="GCHandle"/> for the input values
        /// </summary>
        protected readonly GCHandle InValuesHandle;
        /// <summary>
        /// The <see cref="GCHandle"/> for the output values
        /// </summary>
        protected readonly GCHandle OutValuesHandle;

        private Color TempColor1;
        private Color TempColor2;

        #endregion

        /// <summary>
        /// Creates a new instance of the <see cref="ColorConverter"/> class
        /// </summary>
        /// <param name="inColor">The input color</param>
        /// <param name="outColor">The output color</param>
        public ColorConverter(Color inColor, Color outColor)
        {
            if (inColor == null || outColor == null) throw new ArgumentNullException();

            if (!IsInitiated) Init();

            InColor = inColor;
            OutColor = outColor;
            InValuesHandle = GCHandle.Alloc(InColor.Values, GCHandleType.Pinned);
            OutValuesHandle = GCHandle.Alloc(OutColor.Values, GCHandleType.Pinned);
            InValues = (double*)InValuesHandle.AddrOfPinnedObject();
            OutValues = (double*)OutValuesHandle.AddrOfPinnedObject();
            Data = new ConversionData(InColor, OutColor);

            var types = new Type[] { typeof(ColorConverter), typeof(double*), typeof(double*), typeof(ConversionData) };
            DynamicMethod convMethod = new DynamicMethod("ConversionMethod", null, types, typeof(ColorConverter));
            GetConversionMethod(convMethod.GetILGenerator());
            ConversionMethod = convMethod.CreateDelegate(typeof(ConversionDelegate), this) as ConversionDelegate;
        }

        /// <summary>
        /// Sets the conversion method with the provided <see cref="ILGenerator"/>
        /// </summary>
        protected virtual void GetConversionMethod(ILGenerator CMIL)
        {
            var iccIn = InColor.Space as ColorspaceICC;
            var iccOut = OutColor.Space as ColorspaceICC;

            if (iccIn != null && iccOut != null)
            {
                if (iccIn.Profile.Equals(iccOut.Profile)) SingleICCProfile(CMIL, iccIn.Profile);
                else DuaICCProfile(CMIL, iccIn.Profile, iccOut.Profile);
            }
            else if (iccIn != null) SingleICCProfile(CMIL, iccIn.Profile);
            else if (iccOut != null) SingleICCProfile(CMIL, iccOut.Profile);
            else
            {
                var colCreator = new ConversionCreator_Color(CMIL, Data, InColor, OutColor);
                colCreator.SetConversionMethod();
            }
        }

        #region Find conversion type
        
        private void DuaICCProfile(ILGenerator CMIL, ICCProfile profile1, ICCProfile profile2)
        {
            var inType = InColor.GetType();
            var outType = OutColor.GetType();

            const ProfileClassName abst = ProfileClassName.Abstract;
            const ProfileClassName devl = ProfileClassName.DeviceLink;

            if (profile1.Class == abst || profile2.Class == abst)
            {
                if (profile1.Class != profile2.Class || profile1.PCS != profile2.PCS)
                    throw new ConversionSetupException();

                if (inType == profile1.PCSType && outType == profile1.PCSType)
                {
                    var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                    c.SetConversionMethod();
                }
                else throw new ConversionSetupException();
            }
            else if (profile1.Class == devl || profile2.Class == devl)
            {
                if (profile1.Class != profile2.Class || profile1.PCS != profile2.PCS
                    || profile1.DataColorspace != profile2.DataColorspace)
                    throw new ConversionSetupException();

                if (inType == profile1.DataColorspaceType && outType == profile1.PCSType)
                {
                    var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                    c.SetConversionMethod();
                }
                else throw new ConversionSetupException();
            }
            else
            {
                if (inType == profile1.DataColorspaceType)
                {
                    if (outType == profile2.DataColorspaceType) DuaICCProfile_Data_Data(CMIL, profile1, profile2);
                    else if (outType == profile2.PCSType) DuaICCProfile_Data_PCS(CMIL, profile1, profile2);
                    else throw new ConversionSetupException(); //Should never happen because of Color checks
                }
                else if (inType == profile1.PCSType)
                {
                    if (outType == profile2.DataColorspaceType) DuaICCProfile_PCS_Data(CMIL, profile1, profile2);
                    else if (outType == profile2.PCSType) DuaICCProfile_PCS_PCS(CMIL, profile1, profile2);
                    else throw new ConversionSetupException(); //Should never happen because of Color checks
                }
                else throw new ConversionSetupException(); //Should never happen because of Color checks
            }
        }

        private void DuaICCProfile_PCS_PCS(ILGenerator CMIL, ICCProfile profile1, ICCProfile profile2)
        {
            //PCS1 == PCS2 will result in an assign
            //PCS1 != PCS2 will result in a conversion
            var c = new ConversionCreator_Color(CMIL, Data, InColor, OutColor);
            c.SetConversionMethod();
        }

        private void DuaICCProfile_PCS_Data(ILGenerator CMIL, ICCProfile profile1, ICCProfile profile2)
        {
            if (profile1.PCS == profile2.PCS)
            {
                var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                c.SetConversionMethod();
            }
            else
            {
                TempColor1 = profile2.GetPCSColor(true);
                var c1 = new ConversionCreator_Color(CMIL, Data, InColor, TempColor1, false);
                c1.SetConversionMethod();
                var c2 = new ConversionCreator_ICC(c1, TempColor1, OutColor, true);
                c2.SetConversionMethod();
            }
        }

        private void DuaICCProfile_Data_PCS(ILGenerator CMIL, ICCProfile profile1, ICCProfile profile2)
        {
            if (profile1.PCS == profile2.PCS)
            {
                var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                c.SetConversionMethod();
            }
            else
            {
                TempColor1 = profile1.GetPCSColor(true);
                var c1 = new ConversionCreator_ICC(CMIL, Data, InColor, TempColor1, false);
                c1.SetConversionMethod();
                var c2 = new ConversionCreator_Color(c1, TempColor1, OutColor, true);
                c2.SetConversionMethod();
            }
        }

        private void DuaICCProfile_Data_Data(ILGenerator CMIL, ICCProfile profile1, ICCProfile profile2)
        {
            if (profile1.PCS == profile2.PCS)
            {
                TempColor1 = profile1.GetPCSColor(true);
                var c1 = new ConversionCreator_ICC(CMIL, Data, InColor, TempColor1, false);
                c1.SetConversionMethod();
                var c2 = new ConversionCreator_ICC(c1, TempColor1, OutColor, true);
                c1.SetConversionMethod();
            }
            else
            {
                TempColor1 = profile1.GetPCSColor(true);
                TempColor2 = profile2.GetPCSColor(true);

                var c1 = new ConversionCreator_ICC(CMIL, Data, InColor, TempColor1, false);
                c1.SetConversionMethod();

                var c2 = new ConversionCreator_Color(c1, TempColor1, TempColor2, false);
                c2.SetConversionMethod();

                var c3 = new ConversionCreator_ICC(c2, TempColor2, OutColor, true);
                c3.SetConversionMethod();
            }
        }

        private void SingleICCProfile(ILGenerator CMIL, ICCProfile profile)
        {
            var inType = InColor.GetType();
            var outType = OutColor.GetType();
            
            if (profile.Class == ProfileClassName.Abstract)
            {
                if (inType == profile.PCSType && outType == profile.PCSType)
                {
                    var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                    c.SetConversionMethod();
                }
                else throw new ConversionSetupException();
            }
            else if (profile.Class == ProfileClassName.DeviceLink)
            {
                if (inType == profile.DataColorspaceType && outType == profile.PCSType)
                {
                    var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                    c.SetConversionMethod();
                }
                else throw new ConversionSetupException();
            }
            else
            {
                if (inType == profile.PCSType)
                {
                    if (outType == profile.DataColorspaceType)
                    {
                        var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                        c.SetConversionMethod();
                    }
                    else
                    {
                        var c = new ConversionCreator_Color(CMIL, Data, InColor, OutColor);
                        c.SetConversionMethod();
                    }
                }
                else if (inType == profile.DataColorspaceType)
                {
                    if (outType == profile.PCSType)
                    {
                        var c = new ConversionCreator_ICC(CMIL, Data, InColor, OutColor);
                        c.SetConversionMethod();
                    }
                    else
                    {
                        TempColor1 = profile.GetPCSColor(true);
                        var c1 = new ConversionCreator_ICC(CMIL, Data, InColor, TempColor1, false);
                        c1.SetConversionMethod();
                        var c2 = new ConversionCreator_Color(c1, TempColor1, OutColor, true);
                        c2.SetConversionMethod();
                    }
                }
                else
                {
                    if (outType == profile.DataColorspaceType)
                    {
                        TempColor1 = profile.GetPCSColor(true);
                        var c1 = new ConversionCreator_Color(CMIL, Data, InColor, TempColor1, false);
                        c1.SetConversionMethod();
                        var c2 = new ConversionCreator_ICC(c1, TempColor1, OutColor, true);
                        c2.SetConversionMethod();
                    }
                    else
                    {
                        var c = new ConversionCreator_Color(CMIL, Data, InColor, OutColor);
                        c.SetConversionMethod();
                    }
                }
            }
        }

        #endregion

        #region Dispose

        /// <summary>
        /// Finalizer of the <see cref="ColorConverter"/> class
        /// </summary>
        ~ColorConverter()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases all allocated resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases all allocated resources
        /// </summary>
        /// <param name="managed">True if called by user, false if called by finalizer</param>
        protected virtual void Dispose(bool managed)
        {
            if (!IsDisposed)
            {
                if (InValuesHandle.IsAllocated) InValuesHandle.Free();
                if (OutValuesHandle.IsAllocated) OutValuesHandle.Free();
                if (Data != null) Data.Dispose();

                if (managed)
                {
                    ConversionMethod = null;
                    Data = null;
                }
                IsDisposed = true;
            }
        }

        #endregion

        /// <summary>
        /// Converts the colors given in the constructor
        /// </summary>
        public void Convert()
        {
            ConversionMethod(InValues, OutValues, Data);
        }
    }
}
