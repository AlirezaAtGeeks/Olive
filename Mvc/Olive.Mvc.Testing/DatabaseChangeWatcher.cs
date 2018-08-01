﻿using Olive.Entities.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Olive.Mvc.Testing
{
    class DatabaseChangeWatcher
    {
        static List<XElement> Changes = new List<XElement>();

        static DatabaseChangeWatcher()
        {
            DatabaseStateChangeCommand.ExecutedChangeCommand += DatabaseStateChangeCommand_ExecutedChangeCommand;
        }

        static void DatabaseStateChangeCommand_ExecutedChangeCommand(DatabaseStateChangeCommand change)
        {
            var node = new XElement("Change");
            if (change.CommandType != CommandType.Text)
                node.Add(new XAttribute("Type", change.CommandType.ToString()));

            node.Add(new XAttribute("Command", change.CommandText));

            foreach (var p in change.Params)
                node.Add(new XElement("Param",
                    new XAttribute("Name", p.ParameterName),
                    new XAttribute("Value", p.Value),
                    new XAttribute("Type", p.DbType)));

            Changes.Add(node);
        }

        internal static void Restart() => Changes.Clear();

        internal static async Task DispatchChanges()
        {
            var response = new XElement("Changes", Changes).ToString();
            Changes.Clear();
            await Context.Current.Response().EndWith(response, "text/xml");
        }

        internal static void RunChanges()
        {
            try
            {
                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(Context.Current.Request().Param("Data"));
                var xmlNodeList = xmlDocument.GetElementsByTagName("Changes")[0];

                foreach (XmlElement xmlElement in xmlNodeList.ChildNodes)
                {
                    var command = xmlElement.GetAttribute("Command").Replace("&#xD;&#xA;", Environment.NewLine);
                    var dataParameters = new List<SqlParameter>();

                    foreach (XmlElement innerXmlElement in xmlElement.ChildNodes)
                    {
                        var value = innerXmlElement.GetAttribute("Value");
                        var sqlDbType = innerXmlElement.GetAttribute("Type").To<DbType>();

                        var sqlParameter = new SqlParameter
                        {
                            DbType = sqlDbType,
                            Value = value.IsEmpty() ? (object)DBNull.Value : value,
                            ParameterName = innerXmlElement.GetAttribute("Name"),
                        };

                        switch (sqlDbType)
                        {
                            case DbType.DateTime:
                                sqlParameter.DbType = DbType.DateTime;
                                sqlParameter.SqlDbType = SqlDbType.DateTime;
                                sqlParameter.Value = sqlParameter.Value?.ToString().To<DateTime>();
                                break;
                            case DbType.Guid:
                                sqlParameter.DbType = DbType.Guid;
                                sqlParameter.SqlDbType = SqlDbType.UniqueIdentifier;
                                sqlParameter.Value = sqlParameter.Value?.ToString().To<Guid>();
                                break;
                            case DbType.DateTime2:
                                sqlParameter.DbType = DbType.DateTime2;
                                sqlParameter.SqlDbType = SqlDbType.DateTime2;
                                sqlParameter.Value = sqlParameter.Value?.ToString().To<DateTime>();
                                break;
                        }

                        dataParameters.Add(sqlParameter);
                    }

                    throw new NotImplementedException("In the XML, save the data provider type and connection string key, so it can be executed here.");
                    // DataAccessor.ExecuteNonQuery(command, CommandType.Text, dataParameters.ToArray());
                }

                Cache.Current.ClearAll();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }
}