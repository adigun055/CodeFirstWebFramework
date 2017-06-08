﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;

namespace CodeFirstWebFramework {

	/// <summary>
	/// Class to hold a module name, for use in templates
	/// </summary>
	public class ModuleName {
		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="name"></param>
		public ModuleName(string name) {
			Name = name;
		}
		/// <summary>
		/// Name of module
		/// </summary>
		public string Name;
		/// <summary>
		/// Uncamelled name for display
		/// </summary>
		public string UnCamelName {
			get { return Name.UnCamel(); }
		}
	}

	/// <summary>
	/// Desribes a namespace with AppModules which makes up a WebApp
	/// </summary>
	public class Namespace {
		Dictionary<string, Type> appModules;	// List of all AppModule types by name ("Module" stripped off end)
		Dictionary<string, Table> tables;
		Dictionary<Field, ForeignKeyAttribute> foreignKeys;
		List<Assembly> assemblies;
		List<ModuleName> moduleNames;

		/// <summary>
		/// List of module names for templates (e.g. to auto-generate a module menu)
		/// </summary>
		public IEnumerable<ModuleName> Modules {
			get { return moduleNames; }
		}

		/// <summary>
		/// Namespace name
		/// </summary>
		public string Name { get; private set; }

		/// <summary>
		/// Constructor - uses reflection to get the information
		/// </summary>
		public Namespace(string name) {
			Name = name;
			appModules = new Dictionary<string, Type>();
			assemblies = new List<Assembly>();
			tables = new Dictionary<string, Table>();
			foreignKeys = new Dictionary<Field, ForeignKeyAttribute>();
			List<Type> views = new List<Type>();
			moduleNames = new List<ModuleName>();
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				bool relevant = false;
				foreach (Type t in assembly.GetTypes().Where(t => t.Namespace == name && !t.IsAbstract)) {
					relevant = true;
					if (t.IsSubclassOf(typeof(AppModule))) {
						string n = t.Name;
						if (n.EndsWith("Module"))
							n = n.Substring(0, n.Length - 6);
						appModules[n.ToLower()] = t;
						moduleNames.Add(new ModuleName(n));
					}
					// Process all subclasses of JsonObject with Table attribute in module assembly
					if (t.IsSubclassOf(typeof(JsonObject))) {
						if (t.IsDefined(typeof(TableAttribute), false)) {
							processTable(t, null);
						} else if (t.IsDefined(typeof(ViewAttribute), false)) {
							views.Add(t);
						}
					}
				}
				if (relevant)
					assemblies.Add(assembly);
			}
			// Add any tables defined in the framework module, but not in the given module
			foreach (Type tbl in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(JsonObject)))) {
				if (!tbl.IsDefined(typeof(TableAttribute), false) || tables.ContainsKey(tbl.Name))
					continue;
				processTable(tbl, null);
			}
			// Populate the foreign key attributes
			foreach (Field fld in foreignKeys.Keys) {
				ForeignKeyAttribute fk = foreignKeys[fld];
				Table tbl = TableFor(fk.Table);
				fld.ForeignKey = new ForeignKey(tbl, tbl.Fields[0]);
			}
			// Now do the Views (we assume no views in the framework module)
			foreach (Type tbl in views) {
				processTable(tbl, tbl.GetCustomAttribute<ViewAttribute>());
			}
			foreignKeys = null;
		}

		/// <summary>
		/// Returns the type of the Settings record to be stored in the database.
		/// If there is one in the namespace, returns that, otherwise CodeFirstWebFramework.Settings
		/// </summary>
		public Type GetSettingsType() {
			foreach (Assembly assembly in assemblies) {
				foreach (Type t in assembly.GetTypes().Where(t => t.Namespace == Name && !t.IsAbstract && t.IsSubclassOf(typeof(Settings)))) {
					return t;
				}
			}
			return typeof(Settings);
		}

		/// <summary>
		/// Returns the the Database object to use.
		/// If there is one in the namespace, returns an instance of that, otherwise an instance of CodeFirstWebFramework.Database
		/// </summary>
		/// <param name="server">ConfigServer to pass to the database constructor</param>
		public Database GetDatabase(ServerConfig server) {
			return (Database)Activator.CreateInstance(GetDatabaseType(), server);
		}

		/// <summary>
		/// Returns the type of the Database record to be stored in the database.
		/// If there is one in the namespace, returns that, otherwise CodeFirstWebFramework.Database
		/// </summary>
		public Type GetDatabaseType() {
			foreach (Assembly assembly in assemblies) {
				foreach (Type t in assembly.GetTypes().Where(t => t.Namespace == Name && !t.IsAbstract && t.IsSubclassOf(typeof(Database)))) {
					return t;
				}
			}
			return typeof(Database);
		}

		/// <summary>
		/// Get the AppModule for a module name from the url
		/// </summary>
		public Type GetModuleType(string name) {
			name = name.ToLower();
			return appModules.ContainsKey(name) ? appModules[name] : null;
		}

		/// <summary>
		/// Dictionary of tables defined in this namespace
		/// </summary>
		public Dictionary<string, Table> Tables {
			get { return new Dictionary<string, Table>(tables); }
		}

		/// <summary>
		/// List of the table names
		/// </summary>
		public IEnumerable<string> TableNames {
			get { return tables.Where(t => !t.Value.IsView).Select(t => t.Key); }
		}

		/// <summary>
		/// List of the view names
		/// </summary>
		public IEnumerable<string> ViewNames {
			get { return tables.Where(t => t.Value.IsView).Select(t => t.Key); }
		}

		/// <summary>
		/// Find a Table object by name - throw if not found
		/// </summary>
		public Table TableFor(string name) {
			Table table;
			Utils.Check(tables.TryGetValue(name, out table), "Table '{0}' does not exist", name);
			return table;
		}

		/// <summary>
		/// Generate Table or View object for a C# class
		/// </summary>
		void processTable(Type tbl, ViewAttribute view) {
			List<Field> fields = new List<Field>();
			Dictionary<string, List<Tuple<int, Field>>> indexes = new Dictionary<string, List<Tuple<int, Field>>>();
			List<Tuple<int, Field>> primary = new List<Tuple<int, Field>>();
			string primaryName = null;
			processFields(tbl, ref fields, ref indexes, ref primary, ref primaryName);
			if (primary.Count == 0) {
				primary.Add(new Tuple<int, Field>(0, fields[0]));
				primaryName = "PRIMARY";
			}
			List<Index> inds = new List<Index>(indexes.Keys
				.OrderBy(k => k)
				.Select(k => new Index(k, indexes[k]
					.OrderBy(i => i.Item1)
					.Select(i => i.Item2)
					.ToArray())));
			inds.Insert(0, new Index(primaryName, primary
					.OrderBy(i => i.Item1)
					.Select(i => i.Item2)
					.ToArray()));
			if (view != null) {
				Table updateTable = null;
				// If View is based on a Table class, use that as the update table
				for (Type t = tbl.BaseType; t != typeof(Object); t = t.BaseType) {
					if(t.IsDefined(typeof(TableAttribute), false)) {
						if(tables.TryGetValue(t.Name, out updateTable))
							break;
					}
				}
				if(updateTable == null)
					tables.TryGetValue(Regex.Replace(tbl.Name, "^.*_", ""), out updateTable);
				tables[tbl.Name] = new View(tbl.Name, fields.ToArray(), inds.ToArray(), view.Sql, updateTable);
			} else {
				tables[tbl.Name] = new Table(tbl.Name, fields.ToArray(), inds.ToArray());
			}
		}

		/// <summary>
		/// Update the field, index, etc. information for a C# type.
		/// Process base classes first.
		/// </summary>
		void processFields(Type tbl, ref List<Field> fields, ref Dictionary<string, List<Tuple<int, Field>>> indexes, ref List<Tuple<int, Field>> primary, ref string primaryName) {
			if (tbl.BaseType != typeof(JsonObject))	// Process base types first
				processFields(tbl.BaseType, ref fields, ref indexes, ref primary, ref primaryName);
			foreach (FieldInfo field in tbl.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)) {
				PrimaryAttribute pk;
				Field fld = Field.FieldFor(field, out pk);
				if (fld == null)
					continue;
				if (pk != null) {
					primary.Add(new Tuple<int, Field>(pk.Sequence, fld));
					Utils.Check(primaryName == null || primaryName == pk.Name, "2 Primary keys defined on {0}", tbl.Name);
					primaryName = pk.Name;
				}
				foreach (UniqueAttribute a in field.GetCustomAttributes<UniqueAttribute>()) {
					List<Tuple<int, Field>> index;
					if (!indexes.TryGetValue(a.Name, out index)) {
						index = new List<Tuple<int, Field>>();
						indexes[a.Name] = index;
					}
					index.Add(new Tuple<int, Field>(a.Sequence, fld));
				}
				ForeignKeyAttribute fk = field.GetCustomAttribute<ForeignKeyAttribute>();
				if (fk != null)
					foreignKeys[fld] = fk;
				fields.Add(fld);
			}
		}
	}
}
