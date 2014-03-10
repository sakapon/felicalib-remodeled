﻿/*
 felicalib - FeliCa access wrapper library

 Copyright (c) 2007-2010, Takuya Murakami, All rights reserved.

 Redistribution and use in source and binary forms, with or without
 modification, are permitted provided that the following conditions are
 met:

 1. Redistributions of source code must retain the above copyright notice,
    this list of conditions and the following disclaimer. 

 2. Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution. 

 3. Neither the name of the project nor the names of its contributors
    may be used to endorse or promote products derived from this software
    without specific prior written permission. 

 THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

//
// Porting to x64 systems by DeForest(Hirokazu Hayashi)
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace FelicaLib
{
    static class NativeMethods
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPWStr)]string lpFileName);
        [DllImport("kernel32", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);
    }

    static class UnsafeNativeMethods
    {
        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)]string lpProcName);
    }

    /// <summary>
    /// ネイティブ関数を .NET 向けに拡張します。
    /// </summary>
    /// <remarks>
    /// http://msdn.microsoft.com/ja-jp/library/cc429019.aspx
    /// </remarks>
    static class NativeMethodsHelper
    {
        /// <summary>
        /// 指定された実行可能モジュールを、呼び出し側プロセスのアドレス空間内にマップします。
        /// </summary>
        /// <param name="fileName">モジュールのファイル名。</param>
        /// <returns>モジュールのハンドル。</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        public static IntPtr LoadLibrary(string fileName)
        {
            var ptr = NativeMethods.LoadLibrary(fileName);
            if (ptr == IntPtr.Zero)
            {
                var hResult = Marshal.GetHRForLastWin32Error();
                throw Marshal.GetExceptionForHR(hResult);
            }
            return ptr;
        }

        /// <summary>
        /// ロード済みの DLL モジュールの参照カウントを 1 つ減らします。
        /// </summary>
        /// <param name="module">DLL モジュールのハンドル。</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        public static void FreeLibrary(IntPtr module)
        {
            var result = NativeMethods.FreeLibrary(module);
            if (!result)
            {
                var hResult = Marshal.GetHRForLastWin32Error();
                throw Marshal.GetExceptionForHR(hResult);
            }
        }

        /// <summary>
        /// DLL が持つ、指定されたエクスポート済み関数のアドレスを取得します。
        /// </summary>
        /// <param name="module">DLL モジュールのハンドル。</param>
        /// <param name="procName">関数名。</param>
        /// <returns>DLL のエクスポート済み関数のアドレス。</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        public static IntPtr GetProcAddress(IntPtr module, string procName)
        {
            var ptr = UnsafeNativeMethods.GetProcAddress(module, procName);
            if (ptr == IntPtr.Zero)
            {
                var hResult = Marshal.GetHRForLastWin32Error();
                throw Marshal.GetExceptionForHR(hResult);
            }
            return ptr;
        }

        /// <summary>
        /// DLL が持つ、指定されたエクスポート済み関数をデリゲートとして取得します。
        /// </summary>
        /// <typeparam name="T">デリゲートの型。</typeparam>
        /// <param name="module">DLL モジュールのハンドル。</param>
        /// <param name="procName">関数名。</param>
        /// <returns>デリゲート。</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        public static T GetDelegate<T>(IntPtr module, string procName) where T : class
        {
            var proc = GetProcAddress(module, procName);
            return Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
        }
    }

    /// <summary>
    /// FeliCa のシステム コードを表します。
    /// </summary>
    public enum FelicaSystemCode
    {
        /// <summary>すべて。</summary>
        Any = 0xFFFF,
        /// <summary>共通領域。</summary>
        Common = 0xFE00,
        /// <summary>サイバネ領域。</summary>
        Cybernetics = 0x0003,

        /// <summary>Edy。共通領域を使用します。</summary>
        Edy = Common,
        /// <summary>Suica。サイバネ領域を使用します。</summary>
        Suica = Cybernetics,
        /// <summary>QUICPay。</summary>
        QuicPay = 0x04C1,
    }

    /// <summary>
    /// FeliCa を通じて IC カードからデータを読み取るためのクラスを表します。
    /// </summary>
    public class Felica : IDisposable
    {
        #region DLL およびデリゲート

        // 遅延ロード用Delegate定義
        delegate IntPtr Pasori_open(string dummy);
        delegate int Pasori_close(IntPtr p);
        delegate int Pasori_init(IntPtr p);
        delegate IntPtr Felica_polling(IntPtr p, ushort systemcode, byte rfu, byte time_slot);
        delegate void Felica_free(IntPtr f);
        delegate void Felica_getidm(IntPtr f, byte[] data);
        delegate void Felica_getpmm(IntPtr f, byte[] data);
        delegate int Felica_read_without_encryption02(IntPtr f, int servicecode, int mode, byte addr, byte[] data);

        // 遅延ロード用Delegate
        Pasori_open pasori_open;
        Pasori_close pasori_close;
        Pasori_init pasori_init;
        Felica_polling felica_polling;
        Felica_free felica_free;
        Felica_getidm felica_getidm;
        Felica_getpmm felica_getpmm;
        Felica_read_without_encryption02 felica_read_without_encryption02;

        void LoadDllAndDelegates()
        {
            _pModule = NativeMethodsHelper.LoadLibrary(szDLLname);

            pasori_open = NativeMethodsHelper.GetDelegate<Pasori_open>(_pModule, "pasori_open");
            pasori_close = NativeMethodsHelper.GetDelegate<Pasori_close>(_pModule, "pasori_close");
            pasori_init = NativeMethodsHelper.GetDelegate<Pasori_init>(_pModule, "pasori_init");
            felica_polling = NativeMethodsHelper.GetDelegate<Felica_polling>(_pModule, "felica_polling");
            felica_free = NativeMethodsHelper.GetDelegate<Felica_free>(_pModule, "felica_free");
            felica_getidm = NativeMethodsHelper.GetDelegate<Felica_getidm>(_pModule, "felica_getidm");
            felica_getpmm = NativeMethodsHelper.GetDelegate<Felica_getpmm>(_pModule, "felica_getpmm");
            felica_read_without_encryption02 = NativeMethodsHelper.GetDelegate<Felica_read_without_encryption02>(_pModule, "felica_read_without_encryption02");
        }

        #endregion

        string szDLLname;
        IntPtr _pModule;
        IntPtr pasorip;
        IntPtr felicap;

        /// <summary>
        /// <see cref="Felica"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        public Felica()
        {
            // x64対応 20100501 - DeForest
            try
            {
                // プラットフォーム別のロードモジュール名決定（x64/x86サポート、Iteniumはサポート外）
                if (System.IntPtr.Size >= 8)    // x64
                {
                    szDLLname = "felicalib64.dll";
                }
                else                // x86
                {
                    szDLLname = "felicalib.dll";
                }
                LoadDllAndDelegates();
            }
            catch (Exception)
            {
                throw new Exception(szDLLname + " をロードできません");
            }

            pasorip = pasori_open(null);
            if (pasorip == IntPtr.Zero)
            {
                throw new Exception(szDLLname + " を開けません");
            }
            if (pasori_init(pasorip) != 0)
            {
                throw new Exception("PaSoRi に接続できません");
            }
        }

        #region IDisposable メンバー

        /// <summary>
        /// オブジェクトを破棄します。
        /// </summary>
        ~Felica()
        {
            Dispose(false);
        }

        /// <summary>
        /// このオブジェクトで使用されているすべてのリソースを解放します。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// このオブジェクトで使用されているリソースを解放します。
        /// </summary>
        /// <param name="disposing">すべてのリソースを解放する場合は <see langword="true"/>。アンマネージ リソースのみを解放する場合は <see langword="false"/>。</param>
        protected virtual void Dispose(bool disposing)
        {
            // 読み込みとは逆の順序でリソースを解放します。
            if (felicap != IntPtr.Zero)
            {
                try
                {
                    felica_free(felicap);
                    felicap = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    // 発生したことのある例外:
                    // System.AccessViolationException
                    Debug.WriteLine(ex);
                }
            }

            if (pasorip != IntPtr.Zero)
            {
                try
                {
                    pasori_close(pasorip);
                    pasorip = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    // 発生したことのある例外:
                    // System.AccessViolationException
                    Debug.WriteLine(ex);
                }
            }

            if (_pModule != IntPtr.Zero)
            {
                try
                {
                    NativeMethodsHelper.FreeLibrary(_pModule);
                    _pModule = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    // 発生したことのある例外:
                    // System.IO.FileNotFoundException
                    Debug.WriteLine(ex);
                }
            }
        }

        #endregion

        /// <summary>
        /// ポーリング
        /// </summary>
        /// <param name="systemcode">システムコード</param>
        public void Polling(FelicaSystemCode systemcode)
        {
            Polling((int)systemcode);
        }

        /// <summary>
        /// ポーリング
        /// </summary>
        /// <param name="systemcode">システムコード</param>
        public void Polling(int systemcode)
        {
            felica_free(felicap);

            felicap = felica_polling(pasorip, (ushort)systemcode, 0, 0);
            if (felicap == IntPtr.Zero)
            {
                throw new Exception("カード読み取り失敗");
            }
        }

        /// <summary>
        /// IDm取得
        /// </summary>
        /// <returns>IDmバイナリデータ</returns>
        public byte[] IDm()
        {
            if (felicap == IntPtr.Zero)
            {
                throw new Exception("no polling executed.");
            }

            byte[] buf = new byte[8];
            felica_getidm(felicap, buf);
            return buf;
        }

        /// <summary>
        /// PMm取得
        /// </summary>
        /// <returns>PMmバイナリデータ</returns>
        public byte[] PMm()
        {
            if (felicap == IntPtr.Zero)
            {
                throw new Exception("no polling executed.");
            }

            byte[] buf = new byte[8];
            felica_getpmm(felicap, buf);
            return buf;
        }

        /// <summary>
        /// 非暗号化領域読み込み
        /// </summary>
        /// <param name="servicecode">サービスコード</param>
        /// <param name="addr">アドレス</param>
        /// <returns>データ</returns>
        public byte[] ReadWithoutEncryption(int servicecode, int addr)
        {
            if (felicap == IntPtr.Zero)
            {
                throw new Exception("no polling executed.");
            }

            byte[] data = new byte[16];
            int ret = felica_read_without_encryption02(felicap, servicecode, 0, (byte)addr, data);
            if (ret != 0)
            {
                return null;
            }
            return data;
        }
    }
}
