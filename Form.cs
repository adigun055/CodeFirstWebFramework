﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodeFirstWebFramework {
	/// <summary>
	/// Attribute to define field display in forms
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class FieldAttribute : Attribute {

		/// <summary>
		/// Constructor
		/// </summary>
		public FieldAttribute() {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="args">Pairs of name, value, passed direct into Options (so must be javascript style starting with lower case letter)</param>
		public FieldAttribute(params object [] args) {
			Utils.Check(args.Length % 2 == 0, "Field arguments must be in pairs");
			for (int i = 0; i < args.Length; i += 2) {
				string name = args[i] as string;
				Utils.Check(!string.IsNullOrWhiteSpace(name), "Field argument {0} is not a string", i);
				Options[name] = args[i + 1].ToJToken();
			}
		}

		/// <summary>
		/// The javascript options for the field
		/// </summary>
		public JObject Options = new JObject();

		/// <summary>
		/// Name of variable containing field value
		/// </summary>
		public string Data {
			get { return Options.AsString("data"); }
			set { Options["data"] = value; }
		}

		/// <summary>
		/// Type of field - see list in default.js
		/// </summary>
		public string Type {
			get { return Options.AsString("type"); }
			set { Options["type"] = value; }
		}

		/// <summary>
		/// Heading/prompt for field (defaults to Data, un camel cased)
		/// </summary>
		public string Heading {
			get { return Options.AsString("heading"); }
			set { Options["heading"] = value; }
		}

		/// <summary>
		/// How many columns for field
		/// </summary>
		public int Colspan {
			get { return Options.AsInt("colspan"); }
			set { Options["colspan"] = value; }
		}

		/// <summary>
		/// True if field is to be in the same row as the previous field
		/// </summary>
		public bool SameRow {
			get { return Options.AsBool("sameRow"); }
			set { Options["sameRow"] = value; }
		}

		/// <summary>
		/// Html attributes to add to field
		/// </summary>
		public string Attributes {
			get { return Options.AsString("attributes"); }
			set { Options["attributes"] = value; }
		}

		/// <summary>
		/// Name of field - should be unique within a form. Defaults to same as Data.
		/// </summary>
		public string Name {
			get { return Options.AsString("name"); }
			set { Options["name"] = value; }
		}

		/// <summary>
		/// Number of characters to allow in input
		/// </summary>
		public int Size {
			get { return Options.AsInt("size"); }
			set { Options["size"] = value; }
		}

		/// <summary>
		/// Set to false to hide the field (in DataTables) or omit it (in other forms)
		/// </summary>
		public bool Visible {
			get { return Options["visible"] == null ? true : Options.AsBool("visible"); }
			set { Options["visible"] = value; }
		}

		/// <summary>
		/// Name of field (allowing for default to Data)
		/// </summary>
		public string FieldName {
			get { return Name ?? Data; }
		}

		/// <summary>
		/// SQL Field definition
		/// </summary>
		public Field Field;

		/// <summary>
		/// Create a FieldAttribute for the given field in a class.
		/// </summary>
		/// <param name="db">Database (needed to retrieve default select options)</param>
		/// <param name="field">FieldInfo definition</param>
		/// <param name="readwrite">True if the user can edit the field</param>
		public static FieldAttribute FieldFor(Database db, FieldInfo field, bool readwrite) {
			Field fld = Field.FieldFor(field);
			if (fld == null)
				return null;
			if (readwrite && (field.IsDefined(typeof(ReadOnlyAttribute)) || field.IsDefined(typeof(DoNotStoreAttribute))))
				readwrite = false;
			FieldAttribute f = field.GetCustomAttribute<FieldAttribute>();
			if (f == null) {
				ForeignKeyAttribute fk = field.GetCustomAttribute<ForeignKeyAttribute>();
				if (fk == null) {
					f = new FieldAttribute();
				} else {
					Table t = db.TableFor(fk.Table);
					string valueName = t.Indexes.Length < 2 ? t.Fields[1].Name :
						t.Indexes[1].Fields.Length < 2 ? t.Indexes[1].Fields[0].Name :
						"CONCAT(" + String.Join(",' ',", t.Indexes[1].Fields.Select(fi => fi.Name).ToArray()) + ")";
					f = new SelectAttribute(db.Query("SELECT " + t.PrimaryKey.Name + " AS id, "
						+ valueName + " AS value FROM " + t.Name
						+ " ORDER BY " + valueName), readwrite);
				}
			}
			f.SetField(fld, readwrite);
			return f;
		}

		/// <summary>
		/// Create a FieldAttribute for the given property in a class.
		/// </summary>
		/// <param name="field">Property definition</param>
		/// <param name="readwrite">True if the user can edit the field</param>
		public static FieldAttribute FieldFor(PropertyInfo field, bool readwrite) {
			Field fld = Field.FieldFor(field);
			if (fld == null)
				return null;
			if (readwrite && (field.IsDefined(typeof(ReadOnlyAttribute)) || field.IsDefined(typeof(DoNotStoreAttribute))))
				readwrite = false;
			FieldAttribute f = field.GetCustomAttribute<FieldAttribute>() ?? new FieldAttribute();
			f.SetField(fld, readwrite);
			return f;
		}

		void SetField(Field fld, bool readwrite) { 
			Field = fld;
			if (Data == null)
				Data = fld.Name;
			if (Type == null) {
				switch (fld.Type.Name) {
					case "Int32":
						Type = readwrite ? "intInput" : "int";
						break;
					case "Decimal":
						Type = readwrite ? "decimalInput" : "decimal";
						break;
					case "Double":
						Type = readwrite ? "doubleInput" : "double";
						break;
					case "Boolean":
						Type = readwrite ? "checkboxInput" : "checkbox";
						break;
					case "DateTime":
						Type = readwrite ? "dateInput" : "date";
						break;
					default:
						Type = fld.Length == 0 ?
							readwrite ? "textAreaInput" : "textArea" :
							readwrite ? "textInput" : "string";
						break;
				}
			}
			if (Type == "textInput" && Size == 0 && fld.Length > 0)
				Size = (int)Math.Floor(fld.Length);
		}
	}

	/// <summary>
	/// Special FieldAttribute for select fields
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class SelectAttribute : FieldAttribute {

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="options">The options - each JObject shold have an id and a value (at least). 
		/// If they have a category, a categorised select is created.</param>
		/// <param name="readwrite">True if the user can input to the field.</param>
		public SelectAttribute(JObjectEnumerable options, bool readwrite) {
			Type = readwrite ? "selectInput" : "select";
			SelectOptions = options;
		}

		/// <summary>
		/// Constructor for an input select.
		/// </summary>
		/// <param name="options">The options - each JObject shold have an id and a value (at least). 
		/// If they have a category, a categorised select is created.</param>
		public SelectAttribute(JObjectEnumerable options)
			: this(options, true) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="options">The options - each JObject shold have an id and a value (at least). 
		/// If they have a category, a categorised select is created.</param>
		/// <param name="readwrite">True if the user can input to the field.</param>
		public SelectAttribute(IEnumerable<JObject> options, bool readwrite)
			: this(new JObjectEnumerable(options), readwrite) {
		}

		/// <summary>
		/// Set the select options.
		/// Each JObject shold have an id and a value (at least). 
		/// If they have a category, a categorised select is created.
		/// </summary>
		public JObjectEnumerable SelectOptions {
			set { Options["selectOptions"] = (JArray)value; }
		}
	}

	/// <summary>
	/// Indicate a field or class is writeable by default, even if it is not part of a Table
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property)]
	public class WriteableAttribute : Attribute {
	}

	/// <summary>
	/// Indicate a field or class is readonly by default, even if it is part of a Table
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class ReadOnlyAttribute : Attribute {
	}

	/// <summary>
	/// Base class for all supported forms
	/// </summary>
	public abstract class BaseForm {

		/// <summary>
		/// Constructor
		/// </summary>
		public BaseForm(AppModule module) {
			Module = module;
			Options = new JObject();
		}

		/// <summary>
		/// Form data passed to javascript.
		/// </summary>
		public JToken Data {
			get { return Options["data"]; }
			set { Options["data"] = value; }
		}

		/// <summary>
		/// Module creating the form
		/// </summary>
		public AppModule Module;

		/// <summary>
		/// Form options passed to javascript.
		/// </summary>
		public JObject Options;

		/// <summary>
		/// Build the form html from a template. By default it uses /modulename/methodname.tmpl, but, if that doesn't
		/// exist, it uses the default template for the form (e.g. /datatable.tmpl).
		/// </summary>
		public abstract void Show();

		/// <summary>
		/// Build the form html from a template. By default it uses /modulename/methodname.tmpl, but, if that doesn't
		/// exist, it uses the default template for the form /formType.tmpl.
		/// </summary>
		protected void Show(string formType) {
			string filename = System.IO.Path.Combine(Module.Module, Module.Method).ToLower();
			if (!Module.Server.FileInfo(filename + ".tmpl").Exists)
				filename = formType.ToLower();
			Module.Form = this;
			Module.WriteResponse(Module.Template(filename, Module), "text/html", System.Net.HttpStatusCode.OK);
		}

	}

	/// <summary>
	/// Normal input form.
	/// </summary>
	public class Form : BaseForm {
		/// <summary>
		/// The fields
		/// </summary>
		private JArray columns;

		/// <summary>
		/// Empty form
		/// </summary>
		/// <param name="module">Owning module</param>
		/// <param name="readwrite">Whether the user can input to some of the fields</param>
		public Form(AppModule module, bool readwrite)
			: base(module) {
			if (!module.HasAccess(module.Info, module.Method + "post", out int accessLevel))
				readwrite = false;
			ReadWrite = readwrite;
			if (!readwrite)
				Options["readonly"] = true;
		}

		/// <summary>
		/// Readwrite form for C# type t
		/// </summary>
		public Form(AppModule module, Type t)
			: this(module, t, true) {
		}

		/// <summary>
		/// Form for C# type t
		/// </summary>
		public Form(AppModule module, Type t, bool readwrite)
			: this(module, readwrite) {
			columns = new JArray();
			Options["columns"] = columns;
			Build(t);
		}

		/// <summary>
		/// Form for C# type t with specific fields in specific order
		/// </summary>
		public Form(AppModule module, Type t, bool readwrite, params string [] fieldNames)
			: this(module, readwrite) {
			columns = new JArray();
			Options["columns"] = columns;
			setTableName(t);
			foreach(string name in fieldNames) {
				Add(t, name);
			}
		}

		/// <summary>
		/// Whether the user can input
		/// </summary>
		public bool ReadWrite;

		/// <summary>
		/// Add a field from a C# class to the form
		/// </summary>
		public FieldAttribute Add(FieldInfo field) {
			return Add(field, ReadWrite && field.DeclaringType.IsDefined(typeof(TableAttribute), false));
		}

		/// <summary>
		/// Add a field from a C# class to the form
		/// </summary>
		public FieldAttribute Add(FieldInfo field, bool readwrite) {
			FieldAttribute f = FieldAttribute.FieldFor(Module.Database, field, readwrite);
			if (f != null)
				columns.Add(f.Options);
			return f;
		}

		/// <summary>
		/// Add a field to the form
		/// </summary>
		public void Add(FieldAttribute f) {
			columns.Add(f.Options);
		}

		bool readWriteFlagForTable(Type t, bool writeable) {
			return ReadWrite && (writeable || t.IsDefined(typeof(TableAttribute), false) || t.IsDefined(typeof(WriteableAttribute), false));
		}

		/// <summary>
		/// Add a field to the form by name
		/// </summary>
		public FieldAttribute Add(Type t, string name) {
			FieldAttribute f = null;
			// Name may be "fieldname/heading"
			string[] parts = name.Split('/');
			FieldInfo fld = t.GetField(parts[0]);
			if (fld == null) {
				PropertyInfo p = t.GetProperty(parts[0]);
				Utils.Check(p != null, "Field {0} not found in type {1}", parts[0], t.Name);
				bool readwrite = p.SetMethod != null && readWriteFlagForTable(p.DeclaringType, p.IsDefined(typeof(WriteableAttribute)));
				f = FieldAttribute.FieldFor(p, readwrite);
			} else {
				f = FieldAttribute.FieldFor(Module.Database, fld, readWriteFlagForTable(fld.DeclaringType, fld.IsDefined(typeof(WriteableAttribute))));
			}
			if (f != null) {
				if (parts.Length > 1)
					f.Heading = parts[1];
				else if (f.FieldName.StartsWith(Options.AsString("table")))	// If name starts with table name, remove table name from heading
					f.Heading = f.FieldName.Substring(Options.AsString("table").Length);
				columns.Add(f.Options);
			}
			return f;
		}

		/// <summary>
		/// Insert a field from a C# class to the form
		/// </summary>
		public void Insert(int position, FieldAttribute f) {
			columns.Insert(position, f.Options);
		}

		/// <summary>
		/// Replace a field from a C# class to the form
		/// </summary>
		public void Replace(int position, FieldAttribute f) {
			columns[position] = f.Options;
		}

		/// <summary>
		/// Return the index of the named field in the form
		/// </summary>
		public int IndexOf(string name) {
			int i = 0;
			foreach(FieldAttribute f in Fields) {
				if (f.FieldName == name)
					return i;
				i++;
			}
			return -1;
		}

		void setTableName(Type t) {
			Type table = t;
			while (table != typeof(JsonObject)) {
				if (table.IsDefined(typeof(TableAttribute), false)) {
					Options["table"] = table.Name;
					Options["id"] = Module.Database.TableFor(table.Name).PrimaryKey.Name;
					break;
				}
				table = table.BaseType;
			}
		}

		/// <summary>
		/// Add all the suitable fields from a C# type to the form
		/// </summary>
		public void Build(Type t) {
			setTableName(t);
			processFields(t);
		}

		/// <summary>
		/// Remove the named field
		/// </summary>
		public void Remove(string name) {
			int i = 0;
			bool found = false;
			foreach (FieldAttribute f in Fields) {
				if (f.FieldName == name) {
					found = true;
					break;
				}
				i++;
			}
			if (found)
				columns.RemoveAt(i);
		}

		/// <summary>
		/// Render the form to the web page, using the appropriate template
		/// </summary>
		public override void Show() {
			Show("Form");
		}

		/// <summary>
		/// All the fields in this form
		/// </summary>
		public IEnumerable<FieldAttribute> Fields {
			get {
				return columns.Select(f => new FieldAttribute() { Options = (JObject)f });
			}
		}

		/// <summary>
		/// Find a field by name
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public FieldAttribute this[string name] {
			get {
				return Fields.FirstOrDefault(f => f.FieldName == name);
			}
		}

		/// <summary>
		/// Decide whether a field should be included - e.g. autoincrement and non-visible fields are excluded by default.
		/// </summary>
		protected virtual bool RequireField(FieldAttribute field) {
			return !field.Field.AutoIncrement && field.Visible;
		}

		/// <summary>
		/// Whether the user can delete record.
		/// </summary>
		public bool CanDelete {
			get { return Options.AsBool("canDelete"); }
			set {
				if (!Module.HasAccess(Module.Info, Module.Method + "delete", out int accessLevel))
					value = false;
				Options["canDelete"] = value;
			}
		}

		/// <summary>
		/// Process all the fields from a type (do any base classes first)
		/// </summary>
		/// <param name="tbl"></param>
		void processFields(Type tbl) {
			if (tbl.BaseType != typeof(JsonObject))	// Process base types first
				processFields(tbl.BaseType);
			bool readwrite = ReadWrite && (tbl.IsDefined(typeof(TableAttribute), false) || tbl.IsDefined(typeof(WriteableAttribute), false));
			foreach (FieldInfo field in tbl.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)) {
				FieldAttribute f = FieldAttribute.FieldFor(Module.Database, field, readwrite || (ReadWrite && field.IsDefined(typeof(WriteableAttribute))));
				if (f != null && RequireField(f))
					columns.Add(f.Options);
			}
		}

	}

	/// <summary>
	/// DataTable (jquery) form - always readonly.
	/// </summary>
	public class DataTableForm : Form {

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="module">Creating module</param>
		/// <param name="t">Type to display in the form</param>
		public DataTableForm(AppModule module, Type t) 
			: base(module, t, false) {
		}

		/// <summary>
		/// Url to call when the user selects a record.
		/// </summary>
		public string Select
		{
			get { return Options.AsString("select"); }
			set {
				if (Module.HasAccess(value))
					Options["select"] = value;
			}
		}

		/// <summary>
		/// Render the form to a web page using the appropriate template
		/// </summary>
		public override void Show() {
			base.Show("DataTable");
		}

		/// <summary>
		/// Non-visible fields are included (so you can search on them)
		/// </summary>
		protected override bool RequireField(FieldAttribute field) {
			return !field.Field.AutoIncrement;
		}

	}

	/// <summary>
	/// List form
	/// </summary>
	public class ListForm : Form {

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="module">Creating module</param>
		/// <param name="t">Type to display in the list</param>
		public ListForm(AppModule module, Type t)
			: base(module, t, true) {
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="module">Creating module</param>
		/// <param name="t">Type to display in the list</param>
		/// <param name="readWrite">Whether the user can update the data</param>
		public ListForm(AppModule module, Type t, bool readWrite)
			: base(module, t, readWrite) {
		}

		/// <summary>
		/// Constructor for C# type t with specific fields in specific order
		/// </summary>
		public ListForm(AppModule module, Type t, bool readwrite, params string[] fieldNames)
			: base(module, t, readwrite, fieldNames) {
		}

		/// <summary>
		/// Url to call when the user selects a record.
		/// </summary>
		public string Select
		{
			get { return Options.AsString("select"); }
			set {
				if (Module.HasAccess(value))
					Options["select"] = value;
			}
		}

		/// <summary>
		/// Render the form to a web page using the appropriate template
		/// </summary>
		public override void Show() {
			base.Show("ListForm");
		}

	}

	/// <summary>
	/// Header detailt form
	/// </summary>
	public class HeaderDetailForm : BaseForm {

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="module">Owning module</param>
		/// <param name="header">C# type in the header</param>
		/// <param name="detail">C# type in the detail</param>
		public HeaderDetailForm(AppModule module, Type header, Type detail) 
		: base(module) {
			Header = new Form(module, header);
			Detail = new ListForm(module, detail);
			Options["header"] = Header.Options;
			Options["detail"] = Detail.Options;
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="module">Owning module</param>
		/// <param name="header">Header form</param>
		/// <param name="detail">Detail form</param>
		public HeaderDetailForm(AppModule module, Form header, ListForm detail)
		: base(module) {
			Header = header;
			Detail = detail;
			Options["header"] = Header.Options;
			Options["detail"] = Detail.Options;
		}

		/// <summary>
		/// The header Form
		/// </summary>
		public Form Header;

		/// <summary>
		/// The Detail ListForm
		/// </summary>
		public ListForm Detail;

		/// <summary>
		/// Render the form to a web page using the appropriate template
		/// </summary>
		public override void Show() {
			Show("HeaderDetailForm");
		}

		/// <summary>
		/// Whether the user can delete record.
		/// </summary>
		public bool CanDelete
		{
			get { return Header.CanDelete; }
			set { Header.CanDelete = value; }
		}


	}

}
