﻿using Humanizer;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace IupMetadata.CodeGenerators.Zig
{
	internal static class ElementGenerator
	{
		public static void Generate(string basePath, IupClass item, IupAttribute[] parentAttributes)
		{
			var template = Templates.element;
			template = template.Replace("{{ElementDocumentation}}", Generator.GetDocumentation(item.Documentation));
			template = template.Replace("{{EnumsDecl}}", GetEnumDecl(item, parentAttributes));
			template = template.Replace("{{CallbacksDecl}}", GetCallbacksDecl(item));
			template = template.Replace("{{Name}}", item.Name);
			template = template.Replace("{{ClassName}}", item.ClassName);
			template = template.Replace("{{NativeType}}", item.NativeType.ToString());
			template = template.Replace("{{InitializerBlock}}", GetInitializerBlock(item, parentAttributes));
			template = template.Replace("{{BodyBlock}}", GetBodyBlock(item, parentAttributes));
			template = template.Replace("{{BodyTraits}}", GetBodyTraits(item));
			template = template.Replace("{{InitializerTraits}}", GetInitializerTraits(item));
			template = template.Replace("{{TestsBlock}}", GetTestsBlock(item));

			var fileName = item.Name.Underscore();
			var path = Path.Combine(basePath, $"elements/{fileName}.zig");
			File.WriteAllText(path, template);

			Generator.Fmt(path);
		}

		private static string GetCallbacksDecl(IupClass item)
		{
			string getZigType(DataType dataType) => dataType switch
			{
				DataType.Void => "void",
				DataType.Int => "i32",
				DataType.RefInt => "*i32",
				DataType.Boolean => "bool",
				DataType.Float => "f32",
				DataType.Double => "f64",
				DataType.Char => "u8",
				DataType.String => "[:0]const u8",
				DataType.Handle => "iup.Element",
				DataType.VoidPtr => "*iup.Unknow",
				DataType.Canvas => "*iup.Canvas",
				_ => throw new NotImplementedException()
			};

			var builder = new StringBuilder();
			foreach (var callback in item.Callbacks)
			{
				var args = new StringBuilder();
				for (int i = 0; i < callback.Arguments.Length; i++)
				{
					args.Append($", arg{i}: {getZigType(callback.Arguments[i])}");
				}

				builder.Append($@"

				{Generator.GetDocumentation(callback.Documentation)}
				pub const On{callback.Name}Fn = fn(self: *Self{args}) anyerror!{getZigType(callback.ReturnType)};");
			}

			return builder.ToString();
		}

		private static string GetInitializerBlock(IupClass item, IupAttribute[] parentAttributes)
		{
			var builder = new StringBuilder();
			foreach (var attribute in item.Attributes.Union(parentAttributes))
			{
				if (attribute.Deprecated) continue;
				builder.AppendLine(GetBodySetBlock(attribute, isInitializer: true));
			}

			foreach (var callback in item.Callbacks)
			{
				builder.AppendLine(GetCallbackSetBlock(callback, isInitializer: true));
			}

			return builder.ToString();
		}

		private static string GetBodyBlock(IupClass item, IupAttribute[] parentAttributes)
		{
			var builder = new StringBuilder();
			foreach (var attribute in item.Attributes.Union(parentAttributes))
			{
				if (attribute.Deprecated) continue;
				builder.AppendLine(GetBodyGetBlock(attribute));
				builder.AppendLine(GetBodySetBlock(attribute, isInitializer: false));
			}

			foreach (var callback in item.Callbacks)
			{
				builder.AppendLine(GetCallbackSetBlock(callback, isInitializer: false));
			}

			return builder.ToString();
		}

		private static (string, string) GetNumberedParamsInfo(IupAttribute attribute)
		{
			return attribute.NumberedAttribute switch
			{
				NumberedAttribute.No => ("", ", .{}"),
				NumberedAttribute.OneID => (", index: i32", ", .{ index }"),
				NumberedAttribute.TwoIDs => (", index_lin: i32, index_col: i32", ", .{ index_lin, index_col }"),
				_ => throw new NotImplementedException()
			};
		}

		private static string GetBodySetBlock(IupAttribute attribute, bool isInitializer)
		{
			if (attribute.CreationOnly && !isInitializer) return string.Empty;
			if (attribute.ReadOnly) return string.Empty;

			var (idArgs, idParams) = GetNumberedParamsInfo(attribute);
			var type = isInitializer ? "*Initializer" : "*Self";
			var ret = isInitializer ? "Initializer" : "void";
			var self = isInitializer ? "self.ref" : "self";
			var @return = isInitializer ? "return self.*;" : "";
			var initializer = isInitializer ? "if (self.last_error) |_| return self.*;" : "";

			var fnName = attribute.WriteOnly && !attribute.CreationOnly ? attribute.Name.Camelize() : $"set{attribute.Name}";

			var decl = (attribute.DataFormat, attribute.DataType) switch
			{
				(DataFormat.Binary, DataType.Int) => $@"

				pub fn {fnName}(self: {type}{idArgs},arg: i32) {ret} {{
					{initializer}
					interop.setIntAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs},arg: [:0]const u8) {ret} {{
					{initializer}
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Boolean) => $@"

				pub fn {fnName}(self: {type}{idArgs},arg: bool) {ret} {{
					{initializer}
					interop.setBoolAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Float) => $@"

				pub fn {fnName}(self: {type}{idArgs},arg: f32) {ret} {{
					{initializer}
					interop.setFloatAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Double) => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: f64) {ret} {{
					{initializer}
					interop.setDoubleAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.VoidPtr) => $@"

				pub fn {fnName}(self: {type}, comptime T: type{idArgs}, arg: ?*T) {ret} {{
					{initializer}
					interop.setPtrAttribute(T, {self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Handle) when attribute.Handle?.ElementName != null => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: *iup.{attribute.Handle?.ElementName}) {ret} {{
					{initializer}
					interop.setHandleAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				pub fn {fnName}HandleName(self: {type}{idArgs}, arg: [:0]const u8) {ret} {{
					{initializer}
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Handle) when attribute.Handle != null && isInitializer => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: anytype) {ret} {{
					{initializer}
					if (interop.validateHandle(.{attribute.Handle.NativeType}, arg)) {{
						interop.setHandleAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					}} else |err| {{
						self.last_error = err;
					}}
					{@return}
				}}

				pub fn {fnName}HandleName(self: {type}{idArgs}, arg: [:0]const u8) {ret} {{
					{initializer}
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Handle) when attribute.Handle != null && !isInitializer => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: anytype) !{ret} {{
					{initializer}
					try interop.validateHandle(.{attribute.Handle.NativeType}, arg);
					interop.setHandleAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				pub fn {fnName}HandleName(self: {type}{idArgs}, arg: [:0]const u8) {ret} {{
					{initializer}
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Handle) when attribute.Handle == null => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: anytype) !{ret} {{
					{initializer}
					interop.setHandleAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				pub fn {fnName}HandleName(self: {type}{idArgs}, arg: [:0]const u8) {ret} {{
					{initializer}
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},arg);
					{@return}
				}}

				",

				(DataFormat.Binary, DataType.Void) => $@"

				pub fn {fnName}(self: {type}{idArgs}) {ret} {{
					{initializer}
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},null);
					{@return}
				}}

				",

				(DataFormat.Size, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs}, width: ?i32, height: ?i32) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var value = Size.intIntToString(&buffer, width, height);
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},value);
					{@return}
				}}

				",

				(DataFormat.Margin, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs}, horiz: i32, vert: i32) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var value = Margin.intIntToString(&buffer, horiz, vert);
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},value);
					{@return}
				}}

				",

				(DataFormat.LinColPosCommaSeparated, DataType.String) => @$"

				pub fn {fnName}(self: {type}{idArgs}, lin: i32, col: i32) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var value = iup.LinColPos.intIntToString(&buffer, lin, col, ',');
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},value);
					{@return}
				}}

				",

				(DataFormat.XYPosCommaSeparated or DataFormat.XYPosColonSeparated, DataType.String) x => @$"

				pub fn {fnName}(self: {type}{idArgs}, x: i32, y: i32) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var value = iup.XYPos.intIntToString(&buffer, x, y, '{(x.DataFormat == DataFormat.XYPosCommaSeparated ? ',' : ':')}');
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},value);
					{@return}
				}}

				",

				(DataFormat.RangeCommaSeparated, DataType.String) => @$"

				pub fn {fnName}(self: {type}{idArgs}, begin: i32, end: i32) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var value = iup.Range.intIntToString(&buffer, begin, end, ',');
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},value);
					{@return}
				}}

				",

				(DataFormat.DialogSize, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs}, width: ?iup.ScreenSize, height: ?iup.ScreenSize) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var str = iup.DialogSize.screenSizeToString(&buffer, width, height);
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},str);
					{@return}
				}}

				",

				(DataFormat.Date, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs}, year: u16, month: u8, day: u8) {ret} {{
					{initializer}
					var buffer: [128]u8 = undefined;
					var value = Date {{ .year = year, .month = month, .day = day }};
					interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},value.toString(&buffer));
					{@return}
				}}

				",

				(DataFormat.Rgb, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs}, rgb: iup.Rgb) {ret} {{
					{initializer}
					interop.setRgb({self}, ""{attribute.AttributeName}""{idParams},rgb);
					{@return}
				}}

				",

				(DataFormat.FloatRangeCommaSeparated, DataType.String) => $@"",
				(DataFormat.Alignment, DataType.String) => $@"",
				(DataFormat.Rect, DataType.String) => $@"",
				(DataFormat.Selection, DataType.String) => $@"",
				(DataFormat.MdiActivate, DataType.String) => $@"",

				(DataFormat.Enum, DataType.Int) => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: ?{attribute.Name}) {ret} {{
					{initializer}
					if (arg) |value| {{
						interop.setIntAttribute({self}, ""{attribute.AttributeName}""{idParams},@enumToInt(value));
					}} else {{
						interop.clearAttribute({self}, ""{attribute.AttributeName}""{idParams});
					}}
					{@return}
				}}
				",

				(DataFormat.Enum, DataType.String) => $@"

				pub fn {fnName}(self: {type}{idArgs}, arg: ?{attribute.Name}) {ret} {{
					{initializer}
					if (arg) |value| switch (value) {{
						{string.Join("\n", attribute.EnumValues.Select(x => $@".{x.Name} => interop.setStrAttribute({self}, ""{attribute.AttributeName}""{idParams},""{x.StrValue}""),"))}
					}} else {{
						interop.clearAttribute({self}, ""{attribute.AttributeName}""{idParams});
					}}
					{@return}
				}}
				",

				//TODO: implement Zig signatures
				(_, DataType.Unknown) => $"",
				(_, DataType.Handle) => $"",

				_ => throw new NotImplementedException($"{attribute.DataType} {attribute.DataFormat}")
			};

			if (string.IsNullOrEmpty(decl)) return string.Empty;

			var builder = new StringBuilder();
			builder.Append(Generator.GetDocumentation(attribute.Documentation));
			builder.Append(decl);

			return builder.ToString();
		}

		private static string GetBodyGetBlock(IupAttribute attribute)
		{
			if (attribute.CreationOnly) return string.Empty;
			if (attribute.WriteOnly) return string.Empty;

			var (idArgs, idParams) = GetNumberedParamsInfo(attribute);

			var decl = (attribute.DataFormat, attribute.DataType) switch
			{
				(DataFormat.Binary, DataType.Int) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) i32 {{
					return interop.getIntAttribute(self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.Binary, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) [:0]const u8 {{
					return interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.Binary, DataType.Boolean) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) bool {{
					return interop.getBoolAttribute(self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.Binary, DataType.Float) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) f32 {{
					return interop.getFloatAttribute(self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.Binary, DataType.Double) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) f64 {{
					return interop.getDoubleAttribute(self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.Binary, DataType.VoidPtr) => $@"

				pub fn get{attribute.Name}(self: *Self, comptime T: type{idArgs}) ?*T {{
					return interop.getPtrAttribute(T, self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.Binary, DataType.Handle) when attribute.Handle?.ElementName == null => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) ?iup.Element {{
					if (interop.getHandleAttribute(self, ""{attribute.AttributeName}""{idParams})) |handle| {{
						return iup.Element.fromHandle(handle);
					}} else {{
						return null;
					}}
				}}

				",

				(DataFormat.Binary, DataType.Handle) when attribute.Handle?.ElementName != null => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) ?*iup.{attribute.Handle.ElementName} {{
					if (interop.getHandleAttribute(self, ""{attribute.AttributeName}""{idParams})) |handle| {{
						return @ptrCast(*iup.{attribute.Handle.ElementName}, handle);
					}} else {{
						return null;
					}}
				}}

				",

				(DataFormat.Size, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) Size {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return Size.parse(str);
				}}

				",

				(DataFormat.Margin, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) Margin {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return Margin.parse(str);
				}}

				",

				(DataFormat.DialogSize, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) iup.DialogSize {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return iup.DialogSize.parse(str);
				}}
				",

				(DataFormat.LinColPosCommaSeparated, DataType.String) => @$"

				pub fn get{attribute.Name}(self: *Self{idArgs}) iup.LinColPos {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return iup.LinColPos.parse(str, ',');
				}}

				",

				(DataFormat.XYPosCommaSeparated or DataFormat.XYPosColonSeparated, DataType.String) x => @$"

				pub fn get{attribute.Name}(self: *Self{idArgs}) iup.XYPos {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return iup.XYPos.parse(str, '{(x.DataFormat == DataFormat.XYPosCommaSeparated ? ',' : ':')}');
				}}

				",

				(DataFormat.RangeCommaSeparated, DataType.String) => @$"

				pub fn get{attribute.Name}(self: *Self{idArgs}) iup.Range {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return iup.Range.parse(str, ',');
				}}

				",

				(DataFormat.Date, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) ?iup.Date {{
					var str = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					return iup.Date.parse(str);
				}}

				",

				(DataFormat.Rgb, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) ?iup.Rgb {{
					return interop.getRgb(self, ""{attribute.AttributeName}""{idParams});
				}}

				",

				(DataFormat.FloatRangeCommaSeparated, DataType.String) => $@"",
				(DataFormat.Alignment, DataType.String) => $@"",
				(DataFormat.Rect, DataType.String) => $@"",
				(DataFormat.Selection, DataType.String) => $@"",
				(DataFormat.MdiActivate, DataType.String) => $@"",

				(DataFormat.Enum, DataType.Int) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) {attribute.Name} {{
					var ret = interop.getIntAttribute(self, ""{attribute.AttributeName}""{idParams});
					return @intToEnum({attribute.Name}, ret);
				}}

				",

				(DataFormat.Enum, DataType.String) => $@"

				pub fn get{attribute.Name}(self: *Self{idArgs}) ?{attribute.Name} {{
					var ret = interop.getStrAttribute(self, ""{attribute.AttributeName}""{idParams});
					{string.Join("", attribute.EnumValues.Select(x => $@"
					if (std.ascii.eqlIgnoreCase(""{x.StrValue}"", ret)) return .{x.Name};"))}
					return null;
				}}

				",

				//TODO: implement Zig signatures
				(_, DataType.Unknown) => $"",
				(_, DataType.Handle) => $"",

				_ => throw new NotImplementedException($"{attribute.DataType} {attribute.DataFormat}")
			};

			if (string.IsNullOrEmpty(decl)) return string.Empty;

			var builder = new StringBuilder();
			builder.Append(Generator.GetDocumentation(attribute.Documentation));
			builder.Append(decl);

			return builder.ToString();
		}

		private static string GetCallbackSetBlock(IupCallback callback, bool isInitializer)
		{
			var type = isInitializer ? "*Initializer" : "*Self";
			var ret = isInitializer ? "Initializer" : "void";
			var self = isInitializer ? "self.ref" : "self";
			var @return = isInitializer ? "return self.*;" : "";

			return $@"

				{Generator.GetDocumentation(callback.Documentation)}
				pub fn set{callback.Name}Callback(self: {type}, callback: ?On{callback.Name}Fn) {ret} {{
					const Handler = CallbackHandler(Self, On{callback.Name}Fn, ""{callback.AttributeName}"");
					Handler.setCallback({self}, callback);
					{@return}
				}}
			";
		}

		private static string GetInitializerTraits(IupClass item)
		{
			StringBuilder builder = new StringBuilder();

			if (item.ChildrenCount != 0)
			{
				builder.AppendLine(@"

					pub fn setChildren(self: *Initializer, tuple: anytype) Initializer {
						if (self.last_error) |_| return self.*;

						Self.appendChildren(self.ref, tuple) catch |err| {
							self.last_error = err;
						};

						return self.*;
					}
				");
			}

			return builder.ToString();
		}

		private static string GetBodyTraits(IupClass item)
		{
			StringBuilder builder = new StringBuilder();


			if (item.NativeType == NativeType.Image)
			{
				builder.Append(@"

					///
					/// Creates an image to be shown on a label, button, toggle, or as a cursor.
					/// width: Image width in pixels.
					/// height: Image height in pixels.
					/// pixels: Vector containing the value of each pixel. 
					/// IupImage uses 1 value per pixel, IupImageRGB uses 3 values and  IupImageRGBA uses 4 values per pixel.
					/// Each value is always 8 bit.
					/// Origin is at the top-left corner and data is oriented top to bottom, and left to right.
					/// The pixels array is duplicated internally so you can discard it after the call.
					pub fn init(width: i32, height: i32, imgdata: ?[]const u8) Initializer {
						var handle = interop.create_image(Self, width, height, imgdata);
            
						if (handle) |valid| {
							return .{
								.ref = @ptrCast(*Self, valid),
							};
						} else {
							return .{
								.ref = undefined,
								.last_error = Error.NotInitialized
							};
						}
					}
				");
			}
			else
			{
				builder.Append(@"

					///
					/// Creates an interface element given its class name and parameters.
					/// After creation the element still needs to be attached to a container and mapped to the native system so it can be visible.
					pub fn init() Initializer {
						var handle = interop.create(Self);
            
						if (handle) |valid| {
							return .{
								.ref = @ptrCast(*Self, valid),
							};
						} else {
							return .{
								.ref = undefined,
								.last_error = Error.NotInitialized
							};
						}
					}
				");
			}

			if (item.NativeType == NativeType.Control || item.NativeType == NativeType.Dialog)
			{
				builder.Append(@"

					/// 
					/// Displays a dialog in the current position, or changes a control VISIBLE attribute.
					/// For dialogs it is equivalent to call IupShowXY using IUP_CURRENT. See IupShowXY for more details.
					/// For other controls, to call IupShow is the same as setting VISIBLE=YES.
					pub fn show(self: *Self) !void {
						try interop.show(self);
					}

					///
					/// Hides an interface element. This function has the same effect as attributing value ""NO"" to the interface element’s VISIBLE attribute.
					/// Once a dialog is hidden, either by means of IupHide or by changing the VISIBLE attribute or by means of a click in the window close button, the elements inside this dialog are not destroyed, so that you can show the dialog again. To destroy dialogs, the IupDestroy function must be called.
					pub fn hide(self: *Self) void {
						interop.hide(self);
					}

				");
			}

			builder.Append(@"

				/// 
				/// Destroys an interface element and all its children.
				/// Only dialogs, timers, popup menus and images should be normally destroyed, but detached elements can also be destroyed.        
				pub fn deinit(self: *Self) void {
					interop.destroy(self);
				} 

				/// 
				/// Creates (maps) the native interface objects corresponding to the given IUP interface elements.
				/// It will also called recursively to create the native element of all the children in the element's tree.
				/// The element must be already attached to a mapped container, except the dialog. A child can only be mapped if its parent is already mapped.
				/// This function is automatically called before the dialog is shown in IupShow, IupShowXY or IupPopup.
				/// If the element is a dialog then the abstract layout will be updated even if the dialog is already mapped. If the dialog is visible the elements will be immediately repositioned. Calling IupMap for an already mapped dialog is the same as only calling IupRefresh for the dialog.
				/// Calling IupMap for an already mapped element that is not a dialog does nothing.
				/// If you add new elements to an already mapped dialog you must call IupMap for that elements. And then call IupRefresh to update the dialog layout.
				/// If the WID attribute of an element is NULL, it means the element was not already mapped. Some containers do not have a native element associated, like VBOX and HBOX. In this case their WID is a fake value (void*)(-1).
				/// It is useful for the application to call IupMap when the value of the WID attribute must be known, i.e. the native element must exist, before a dialog is made visible.
				/// The MAP_CB callback is called at the end of the IupMap function, after all processing, so it can also be used to create other things that depend on the WID attribute. But notice that for non dialog elements it will be called before the dialog layout has been updated, so the element current size will still be 0x0 (since 3.14).
				pub fn map(self: *Self) !void {
					try interop.map(self);
				} 

			");

			if (item.ChildrenCount != 0)
			{
				builder.AppendLine(@"

					///
					/// Adds a tuple of children
					pub fn appendChildren(self: *Self, tuple: anytype) !void {
						try Impl(Self).appendChildren(self, tuple);
					}

					///
					/// Appends a child on this container
					/// child must be an Element or
					pub fn appendChild(self: *Self, child: anytype) !void {
						try Impl(Self).appendChild(self, child);
					}

					///
					/// Returns a iterator for children elements.
					pub fn children(self: *Self) ChildrenIterator {
						return ChildrenIterator.init(self);
					}

				");
			}

			if (item.NativeType == NativeType.Dialog || item.ClassName == "menu")
			{
				builder.Append(@"

					pub fn popup(self: *Self, x: iup.DialogPosX, y: iup.DialogPosY) !void {
						try interop.popup(self, x, y);
					}

				");
			}

			if (item.NativeType == NativeType.Dialog)
			{
				builder.AppendLine(@"

					pub fn showXY(self: *Self, x: iup.DialogPosX, y: iup.DialogPosY) !void {
						try interop.showXY(self, x, y);
					}
				");

				if (item.ClassName == "messagedlg")
				{
					builder.Append(@"
						pub fn alert(parent: *iup.Dialog, title: ?[:0]const u8, message: [:0]const u8) !void {
							try Impl(Self).messageDialogAlert(parent, title, message);
						}

						pub fn confirm(parent: *iup.Dialog, title: ?[:0]const u8, message: [:0]const u8) !bool {
							return try Impl(Self).messageDialogAlertConfirm(parent, title, message);
						}
					");
				}
			}
			else
			{
				builder.AppendLine(@"

					///
					///
					pub fn getDialog(self: *Self) ?*iup.Dialog {
						return interop.getDialog(self);
					}
				");
			}

			if (item.ClassName == "text" || item.ClassName == "multiline")
			{
				builder.Append(@"

					///
					/// Converts a (lin, col) character positioning into an absolute position. lin and col starts at 1, pos starts at 0. For single line controls pos is always ""col - 1"". (since 3.0)
					pub fn convertLinColToPos(self: *Self, lin : i32, col : i32) ?i32 {
						return Impl(Self).convertLinColToPos(self, lin, col);
					}

					///
					///
					pub fn convertPosToLinCol(self: *Self, pos: i32) ?iup.LinColPos {
						return Impl(Self).convertPosToLinCol(self, pos);
					}

				");
			}

			builder.Append(@"

				///
				/// Returns the the child element that has the NAME attribute equals to the given value on the same dialog hierarchy.
				/// Works also for children of a menu that is associated with a dialog.
				pub fn getDialogChild(self: *Self, byName: [:0]const u8) ?Element {
					return interop.getDialogChild(self, byName);
				}

				///
				/// Updates the size and layout of all controls in the same dialog.
				/// To be used after changing size attributes, or attributes that affect the size of the control. Can be used for any element inside a dialog, but the layout of the dialog and all controls will be updated. It can change the layout of all the controls inside the dialog because of the dynamic layout positioning.
				pub fn refresh(self: *Self) void {
					Impl(Self).refresh(self);
				}

			");

			return builder.ToString();
		}

		private static string GetEnumDecl(IupClass item, IupAttribute[] parentAttributes)
		{
			var builder = new StringBuilder();
			foreach (var attribute in item.Attributes.Union(parentAttributes))
			{
				if (attribute.EnumValues == null || attribute.EnumValues.Length == 0) continue;

				builder.AppendLine(Generator.GetDocumentation(attribute.Documentation));
				builder.AppendLine($@"pub const {attribute.Name} = enum{(attribute.DataType == DataType.Int ? "(i32)" : "")} {{");

				foreach (var value in attribute.EnumValues)
				{
					builder.AppendLine($"{value.Name}{(value.IntValue != null ? $" = {value.IntValue}" : "")},");
				}

				builder.AppendLine($@"}};");
			}

			return builder.ToString();
		}

		private static string GetTestsBlock(IupClass item)
		{
			// Mosts operations on those elements will segfault without propper initialization
			// skiping tests until we don't have a better idea to generate them
			if (new[] { "image", "imagergb", "imagergba", "param", "parambox" }.Any(x => x == item.ClassName)) return "";

			var builder = new StringBuilder();

			foreach (var attribute in item.Attributes)
			{
				if (attribute.CreationOnly || attribute.ReadOnly || attribute.WriteOnly) continue;

				var (setStatement, assertStatement) = GetTestBlock(attribute);
				if (setStatement == null || assertStatement == null) continue;

				var indexArgs = attribute.NumberedAttribute switch
				{
					NumberedAttribute.No => "",
					NumberedAttribute.OneID => "0",
					NumberedAttribute.TwoIDs => "0,0",
					_ => throw new NotImplementedException()
				};

				builder.AppendLine(@$"
					test ""{item.Name} {attribute.Name}"" {{
						try iup.MainLoop.open();
						defer iup.MainLoop.close();

						var item = try (iup.{item.Name}.init().{setStatement}.unwrap());
						defer item.deinit();

						var ret = item.get{attribute.Name}({indexArgs});

						try std.testing.expect({assertStatement});
					}}
				");
			}

			return builder.ToString();
		}

		private static (string, string) GetTestBlock(IupAttribute attribute)
		{
			var indexArgs = attribute.NumberedAttribute switch
			{
				NumberedAttribute.No => "",
				NumberedAttribute.OneID => "0,",
				NumberedAttribute.TwoIDs => "0,0,",
				_ => throw new NotImplementedException()
			};

			var (setStatement, assertStatement) = (attribute.DataFormat, attribute.DataType) switch
			{
				(DataFormat.Binary, DataType.Int) => ($@"set{attribute.Name}({indexArgs}42)", $@"ret == 42"),

				(DataFormat.Binary, DataType.String) => ($@"set{attribute.Name}({indexArgs}""Hello"")", $@"std.mem.eql(u8, ret, ""Hello"")"),

				(DataFormat.Binary, DataType.Boolean) => ($@"set{attribute.Name}({indexArgs}true)", $@"ret == true"),

				(DataFormat.Binary, DataType.Float) => ($@"set{attribute.Name}({indexArgs}3.14)", $@"ret) == @as(f32, 3.14)"),

				(DataFormat.Binary, DataType.Double) => ($@"set{attribute.Name}({indexArgs}3.14)", $@"ret == @as(f64, 3.14)"),

				(DataFormat.Binary, DataType.VoidPtr) => (null, null),

				(DataFormat.Binary, DataType.Handle) => (null, null),

				(DataFormat.Size, DataType.String) => ($@"set{attribute.Name}({indexArgs}9, 10)", $@"ret.width != null and ret.width.? == 9 and ret.height != null and ret.height.? == 10"),

				(DataFormat.Margin, DataType.String) => ($@"set{attribute.Name}({indexArgs}9, 10)", $@"ret.horiz == 9 and ret.vert == 10"),

				(DataFormat.LinColPosCommaSeparated, DataType.String) => ($@"set{attribute.Name}({indexArgs}9, 10)", $@"ret.lin == 9 and ret.col == 10"),

				(DataFormat.XYPosCommaSeparated, DataType.String) => ($@"set{attribute.Name}({indexArgs}9, 10)", $@"ret.x == 9 and ret.y == 10"),

				(DataFormat.RangeCommaSeparated, DataType.String) => ($@"set{attribute.Name}({indexArgs}9, 10)", $@"ret.begin == 9 and ret.end == 10"),

				(DataFormat.DialogSize, DataType.String) => (null, null),
				(DataFormat.Date, DataType.String) => (null, null),

				(DataFormat.Rgb, DataType.String) => ($@"set{attribute.Name}({indexArgs}.{{ .r = 9, .g = 10, .b = 11 }})", $@"ret != null and ret.?.r == 9 and ret.?.g == 10 and ret.?.b == 11"),

				(DataFormat.FloatRangeCommaSeparated, DataType.String) => (null, null),
				(DataFormat.Alignment, DataType.String) => (null, null),
				(DataFormat.Rect, DataType.String) => (null, null),
				(DataFormat.Selection, DataType.String) => (null, null),
				(DataFormat.MdiActivate, DataType.String) => (null, null),

				(DataFormat.Enum, DataType.Int) => ($@"set{attribute.Name}({indexArgs}.{attribute.EnumValues[0].Name})", $@"ret == .{attribute.EnumValues[0].Name}"),
				(DataFormat.Enum, DataType.String) => ($@"set{attribute.Name}({indexArgs}.{attribute.EnumValues[0].Name})", $@"ret != null and ret.? == .{attribute.EnumValues[0].Name}"),

				//TODO: implement Zig signatures
				(_, DataType.Unknown) => (null, null),
				(_, DataType.Handle) => (null, null),

				_ => throw new NotImplementedException($"{attribute.DataType} {attribute.DataFormat}")
			};

			return (setStatement, assertStatement);
		}
	}
}