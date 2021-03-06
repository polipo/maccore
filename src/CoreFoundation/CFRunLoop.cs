//
// CFRunLoop.cs: Main Loop
//
// Authors:
//    Miguel de Icaza (miguel@novell.com)
//    Martin Baulig (martin.baulig@gmail.com)
//
// Copyright 2011, 2012 Xamarin Inc
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
//

using System;
using System.Runtime.InteropServices;

using MonoMac.ObjCRuntime;
using MonoMac.Foundation;

namespace MonoMac.CoreFoundation {

	public enum CFRunLoopExitReason {
		Finished = 1,
		Stopped = 2,
		TimedOut = 3,
		HandledSource = 4
	}

	[StructLayout (LayoutKind.Sequential)]
	internal struct CFRunLoopSourceContext {
		public CFIndex Version;
		public IntPtr Info;
		public IntPtr Retain;
		public IntPtr Release;
		public IntPtr CopyDescription;
		public IntPtr Equal;
		public IntPtr Hash;
		public IntPtr Schedule;
		public IntPtr Cancel;
		public IntPtr Perform;
	}

	public class CFRunLoopSource : CFType {
		protected CFRunLoopSource ()
		{
		}

		internal CFRunLoopSource (IntPtr handle)
			: this (handle, false)
		{
		}

		internal CFRunLoopSource (IntPtr handle, bool owns)
			: base (handle, owns)
		{
		}

		~CFRunLoopSource ()
		{
			Dispose (false);
		}


		[DllImport (Constants.CoreFoundationLibrary)]
		extern static CFIndex CFRunLoopSourceGetOrder (IntPtr source);
		public int Order {
			get {
				return CFRunLoopSourceGetOrder (Handle);
			}
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static void CFRunLoopSourceInvalidate (IntPtr source);
		public void Invalidate ()
		{
			CFRunLoopSourceInvalidate (Handle);
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static int CFRunLoopSourceIsValid (IntPtr source);
		public bool IsValid {
			get {
				return CFRunLoopSourceIsValid (Handle) != 0;
			}
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static void CFRunLoopSourceSignal (IntPtr source);
		public void Signal ()
		{
			CFRunLoopSourceSignal (Handle);
		}
	}

	public abstract class CFRunLoopSourceCustom : CFRunLoopSource {
		GCHandle gch;

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static IntPtr CFRunLoopSourceCreate (IntPtr allocator, int order, IntPtr context);

		protected CFRunLoopSourceCustom ()
		{
			gch = GCHandle.Alloc (this);
			var ctx = new CFRunLoopSourceContext ();
			ctx.Info = GCHandle.ToIntPtr (gch);
			ctx.Schedule = Marshal.GetFunctionPointerForDelegate ((ScheduleCallback)Schedule);
			ctx.Cancel = Marshal.GetFunctionPointerForDelegate ((CancelCallback)Cancel);
			ctx.Perform = Marshal.GetFunctionPointerForDelegate ((PerformCallback)Perform);

			var ptr = Marshal.AllocHGlobal (Marshal.SizeOf (typeof(CFRunLoopSourceContext)));
			try {
				Marshal.StructureToPtr (ctx, ptr, false);
				Handle = CFRunLoopSourceCreate (IntPtr.Zero, 0, ptr);
			} finally {
				Marshal.FreeHGlobal (ptr);
			}

			if (Handle == IntPtr.Zero)
				throw new NotSupportedException ();
		}

		delegate void ScheduleCallback (IntPtr info, IntPtr runLoop, IntPtr mode);
		[MonoPInvokeCallback (typeof(ScheduleCallback))]
		static void Schedule (IntPtr info, IntPtr runLoop, IntPtr mode)
		{
			var source = GCHandle.FromIntPtr (info).Target as CFRunLoopSourceCustom;

			var loop = GetCFObject<CFRunLoop> (runLoop);
			var mstring = GetCFObject<CFString> (mode);

			source.OnSchedule (loop, (string)mstring);
		}

		protected abstract void OnSchedule (CFRunLoop loop, string mode);

		delegate void CancelCallback (IntPtr info, IntPtr runLoop, IntPtr mode);
		[MonoPInvokeCallback (typeof(CancelCallback))]
		static void Cancel (IntPtr info, IntPtr runLoop, IntPtr mode)
		{
			var source = GCHandle.FromIntPtr (info).Target as CFRunLoopSourceCustom;

			var loop = GetCFObject<CFRunLoop> (runLoop);
			var mstring = GetCFObject<CFString> (mode);

			source.OnCancel (loop, (string)mstring);
		}

		protected abstract void OnCancel (CFRunLoop loop, string mode);

		delegate void PerformCallback (IntPtr info);
		[MonoPInvokeCallback (typeof(PerformCallback))]
		static void Perform (IntPtr info)
		{
			var source = GCHandle.FromIntPtr (info).Target as CFRunLoopSourceCustom;
			source.OnPerform ();
		}

		protected abstract void OnPerform ();

		protected override void Dispose (bool disposing)
		{
			if (disposing) {
				if (gch.IsAllocated)
					gch.Free ();
			}
			base.Dispose (disposing);
		}
	}

	public class CFRunLoop : CFType {
		const string ModeDefault = "kCFRunLoopDefaultMode";
		const string ModeCommon = "kCFRunLoopCommonModes";

		static IntPtr CoreFoundationLibraryHandle = Dlfcn.dlopen (Constants.CoreFoundationLibrary, 0);

		static NSString _CFDefaultRunLoopMode;
		public static NSString CFDefaultRunLoopMode {
			get {
				if (_CFDefaultRunLoopMode == null)
					_CFDefaultRunLoopMode = Dlfcn.GetStringConstant (CoreFoundationLibraryHandle, ModeDefault);
				return _CFDefaultRunLoopMode;
			}
		}

		static NSString _CFRunLoopCommonModes;
		public static NSString CFRunLoopCommonModes {
			get {
				if (_CFRunLoopCommonModes == null)
					_CFRunLoopCommonModes = Dlfcn.GetStringConstant (CoreFoundationLibraryHandle, ModeCommon);
				return _CFRunLoopCommonModes;
			}
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static IntPtr CFRunLoopGetCurrent ();

		static public CFRunLoop Current {
			get {
				return GetCFObject<CFRunLoop> (CFRunLoopGetCurrent ());
			}
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static IntPtr CFRunLoopGetMain ();
		
		static public CFRunLoop Main {
			get {
				return GetCFObject<CFRunLoop> (CFRunLoopGetMain ());
			}
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static void CFRunLoopRun ();
		public void Run ()
		{
			CFRunLoopRun ();
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static void CFRunLoopStop (IntPtr loop);
		public void Stop ()
		{
			CFRunLoopStop (Handle);
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static void CFRunLoopWakeUp (IntPtr loop);
		public void WakeUp ()
		{
			CFRunLoopWakeUp (Handle);
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static int CFRunLoopIsWaiting (IntPtr loop);
		public bool IsWaiting {
			get {
				ThrowIfDisposed ();
				return CFRunLoopIsWaiting (Handle) != 0;
			}
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static int CFRunLoopRunInMode (IntPtr mode, double seconds, int returnAfterSourceHandled);
		public CFRunLoopExitReason RunInMode (NSString mode, double seconds, bool returnAfterSourceHandled)
		{
			if (mode == null)
				throw new ArgumentNullException ("mode");

			return (CFRunLoopExitReason) CFRunLoopRunInMode (mode.Handle, seconds, returnAfterSourceHandled ? 1 : 0);
		}

		[Obsolete ("Use the NSString version of CFRunLoop.RunInMode() instead.")]
		public CFRunLoopExitReason RunInMode (string mode, double seconds, bool returnAfterSourceHandled)
		{
			if (mode == null)
				throw new ArgumentNullException ("mode");

			CFString s = new CFString (mode);

			var v = CFRunLoopRunInMode (s.Handle, seconds, returnAfterSourceHandled ? 1 : 0);
			s.Dispose ();

			return (CFRunLoopExitReason) v;
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static void CFRunLoopAddSource (IntPtr loop, IntPtr source, IntPtr mode);
		public void AddSource (CFRunLoopSource source, NSString mode)
		{
			if (mode == null)
				throw new ArgumentNullException ("mode");

			CFRunLoopAddSource (Handle, source.Handle, mode.Handle);
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static bool CFRunLoopContainsSource (IntPtr loop, IntPtr source, IntPtr mode);
		public bool ContainsSource (CFRunLoopSource source, NSString mode)
		{
			if (mode == null)
				throw new ArgumentNullException ("mode");

			return CFRunLoopContainsSource (Handle, source.Handle, mode.Handle);
		}

		[DllImport (Constants.CoreFoundationLibrary)]
		extern static bool CFRunLoopRemoveSource (IntPtr loop, IntPtr source, IntPtr mode);
		public bool RemoveSource (CFRunLoopSource source, NSString mode)
		{
			if (mode == null)
				throw new ArgumentNullException ("mode");

			return CFRunLoopRemoveSource (Handle, source.Handle, mode.Handle);
		}

		internal CFRunLoop (IntPtr handle)
			: this (handle, false)
		{
		}

		[Preserve (Conditional = true)]
		internal CFRunLoop (IntPtr handle, bool owns)
			: base (handle, owns)
		{
		}

		~CFRunLoop ()
		{
			Dispose (false);
		}

		public static bool operator == (CFRunLoop a, CFRunLoop b)
		{
			return Object.Equals (a, b);
		}

		public static bool operator != (CFRunLoop a, CFRunLoop b)
		{
			return !Object.Equals (a, b);
		}

		public override int GetHashCode ()
		{
			return Handle.GetHashCode ();
		}

		public override bool Equals (object other)
		{
			CFRunLoop cfother = other as CFRunLoop;
			if (cfother == null)
				return false;

			return cfother.Handle == Handle;
		}
	}
}
