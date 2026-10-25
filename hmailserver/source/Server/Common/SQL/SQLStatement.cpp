// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#include "stdafx.h"
#include "SQLStatement.h"
#include "SQLCommand.h"

#include "DatabaseSettings.h"
#include "../Util/VariantDateTime.h"
#include "../Util/Time.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   SQLStatement::SQLStatement() :
      type_(STUndefined),
      top_rows_(-1)
   {

   }

   SQLStatement::SQLStatement(eStatementType iType, const String &tableName) :
      type_(iType),
      top_rows_(-1),
      table_(tableName)
   {

   }


   SQLStatement::~SQLStatement()
   {

   }

   void 
   SQLStatement::AddColumn(const String &sName, const String &sValue)
   {
      Column p;

      p.sName = sName;
      p.iType = ColTypeString;
      p.sString = sValue;

      vecColumns.push_back(p);
   }

   void 
   SQLStatement::AddColumn(const String &sName, const String &sValue, int iMaxLength)
   {
      Column p;

      String sCopyOfValue = sValue;

      if (sCopyOfValue.GetLength() > iMaxLength)
         sCopyOfValue = sCopyOfValue.Mid(0, iMaxLength);

      p.sName = sName;
      p.iType = ColTypeString;
      p.sString = sCopyOfValue;

      vecColumns.push_back(p);
   }

   void 
   SQLStatement::AddColumnDate(const String &sName, const DateTime & dtValue)
   {
      String value = Time::GetTimeStampFromDateTime(dtValue);

      Column p;

      p.sName = sName;

      // If the date is older than 1800, don't store it. This is to solve
      // limitations in SQL Server.
      if (dtValue.GetStatus() == DateTime::invalid || dtValue.GetYear() < 1800)
      {
         p.iType = ColTypeRaw;
         p.sString = "NULL";
      }
      else
      {
         p.iType = ColTypeString;
         p.sString = value;
      }

      vecColumns.push_back(p);
   }

   void 
   SQLStatement::AddColumnNULL(const String &sName)
   {
      Column p;

      p.sName = sName;
      p.iType = ColTypeRaw;
      p.sString = "NULL";

      vecColumns.push_back(p);
   }



   void 
   SQLStatement::AddColumn(const String &sName, long lValue)
   {
      Column p;
      p.sName = sName;
      p.iType = ColTypeValue;
      p.iInt = lValue;

      vecColumns.push_back(p);
   }

   void 
   SQLStatement::AddColumnInt64(const String &sName, __int64 lValue)
   {
      Column p;
      p.sName = sName;
      p.iType = ColTypeValue;
      p.iInt = lValue;

      vecColumns.push_back(p);
   }

   void 
   SQLStatement::AddColumn(const String & sName)
   {
      Column p;
      p.sName = sName;
      p.iType = ColTypeUnknown;
   
      vecColumns.push_back(p);
   }

   void
   SQLStatement::AddColumnCommand(const String &column, const String &command)
   {
      Column p;
      p.sName = column;
      p.iType = ColTypeRaw;
      p.sString = command;

      vecColumns.push_back(p);
   }

   SQLCommand
   SQLStatement::GetCommand() const
   {
      DatabaseSettings::SQLDBType dbType = IniFileSettings::Instance()->GetDatabaseType();
      if (dbType == DatabaseSettings::TypeUnknown)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5407, "SQLStatement::GetCommand()", Formatter::Format("Unknown database type: {0}", dbType));
         SQLCommand emtpy;
         return emtpy;
      }

      String sSQL;
      SQLCommand command;

      int parameterValue = 1;
      if (type_ == SQLStatement::STInsert)
      {
         sSQL.append(_T("INSERT INTO "));
         sSQL.append(table_);
         sSQL.append(_T(" "));

         // First add columns
         sSQL.append(_T("("));
         bool first = true;
         for(Column c : vecColumns)
         {
            if (!first)
               sSQL.append(_T(", "));
            else
               first = false;

            sSQL.append(c.sName);
         }
         sSQL.append(_T(") VALUES ("));

         // Now append values
         first = true;
         for(Column c : vecColumns)
         {
            if (!first)
               sSQL += ", ";
            else
               first = false;

            parameterValue++;
            String parameterName = "@" + c.sName + StringParser::IntToString(parameterValue);

            switch (c.iType)
            {
            case ColTypeValue:
               sSQL.append(parameterName);
               command.AddParameter(parameterName, c.iInt);
               break;
            case ColTypeString:
               sSQL.append(parameterName);
               command.AddParameter(parameterName, c.sString);
               break;
            case ColTypeRaw:
               // SQL the server itself wrote, such as NULL or current_timestamp - never a value.
               sSQL.append(c.sString);
               break;
            }
         }

         sSQL.append(_T(")"));

         if (dbType == DatabaseSettings::TypePGServer && !identity_column_.IsEmpty())
         {
            sSQL += " RETURNING " + identity_column_;
         }

      }
      else if (type_ == SQLStatement::STUpdate)
      {
         sSQL.append(_T("UPDATE "));
         sSQL.append(table_);
         sSQL.append(_T(" SET "));

         // First add columns
         bool first = true;
         for(Column c : vecColumns)
         {
            if (!first)
               sSQL.append(_T(", "));
            else
               first = false;

            parameterValue++;
            String parameterName = "@" + c.sName +  StringParser::IntToString(parameterValue);

            sSQL.append(c.sName);
            sSQL.append(_T("="));

            switch (c.iType)
            {
            case ColTypeValue:
               sSQL.append(parameterName);
               command.AddParameter(parameterName, c.iInt);
               break;
            case ColTypeString:
               sSQL.append(parameterName);
               command.AddParameter(parameterName, c.sString);
               break;
            case ColTypeRaw:
               // SQL the server itself wrote, such as NULL or current_timestamp - never a value.
               sSQL.append(c.sString);
               break;
            }
         }
      }
      else if (type_ == SQLStatement::STDelete)
      {
         sSQL = "DELETE FROM ";
         sSQL.append(table_);
      }
      else if (type_ == SQLStatement::STSelect)
      {
         sSQL = "SELECT ";

         if (top_rows_ > -1)
         {
            String value;

            switch (dbType)
            {
            case DatabaseSettings::TypeMSSQLServer:
               // SQL Server 2000 does not support ( and ) around the value.
               value.Format(_T(" TOP %d "), top_rows_);
               sSQL.append(value);
               break;
            case DatabaseSettings::TypeMSSQLCompactEdition:
               // SQL Server Compact Edition 3.5 requires () around the value.
               value.Format(_T(" TOP (%d) "), top_rows_);
               sSQL.append(value);
               break;
            }
         }

         if (vecColumns.size() == 0)
         {
            sSQL += " * ";
         }
         else
         {
            std::vector<Column>::const_iterator it = vecColumns.begin();
            std::vector<Column>::const_iterator itEnd = vecColumns.end();

            bool first = true;

            for(Column c : vecColumns)
            {
               if (!first)
                  sSQL.append(_T(", "));
               else
                  first = false;
               
               sSQL.append(c.sName);
            }
         }

         sSQL.append(_T(" FROM "));
         sSQL.append(table_);

         if (!additional_sql_.IsEmpty())
            sSQL.append(_T(" ") + additional_sql_);
      }

      if (where_clause_columns_.size() != 0)
      {
         sSQL.append(_T(" WHERE "));
         bool first = true;
         for(Column col : where_clause_columns_)
         {
            if (!first)
               sSQL.append(_T(" AND "));
            else
               first = false;

            parameterValue++;
            String parameterName = "@" + col.sName + StringParser::IntToString(parameterValue);

            if (col.iType == ColTypeString)
            {
               switch (dbType)
               {
               case DatabaseSettings::TypeMYSQLServer:
               case DatabaseSettings::TypeMSSQLServer:
               case DatabaseSettings::TypeMSSQLCompactEdition:
                  sSQL += col.sName + " = " + parameterName;
                  break;
               case DatabaseSettings::TypePGServer:
                  sSQL += "lower(" + col.sName + ") = lower(" + parameterName + ")";
                  break;
               }

               command.AddParameter(parameterName, col.sString);
            }
            else if (col.iType == ColTypeValue)
            {
               sSQL += col.sName + " = " + parameterName;
               command.AddParameter(parameterName, col.iInt);
            }
            else if (col.iType == ColTypeRaw)
            {
               // As for a raw column: SQL the server itself wrote.
               sSQL += col.sName + " = " + col.sString;
            }
         }
      }
      else if (!where_.IsEmpty())
      {
         sSQL.append(_T(" WHERE "));
         sSQL.append(where_);
      }

      if (type_ == SQLStatement::STSelect)
      {
         if (top_rows_ > -1)
         {
            HM::DatabaseSettings::SQLDBType DBType = IniFileSettings::Instance()->GetDatabaseType();
            String value;
            switch (DBType)
            {
            case DatabaseSettings::TypePGServer:
               value.Format(_T(" LIMIT %d "), top_rows_);
               sSQL.append(value);
               break;
            case DatabaseSettings::TypeMYSQLServer:
               value.Format(_T(" LIMIT 0, %d "), top_rows_);
               sSQL.append(value);
               break;
            }
         }
      }
   
      command.SetQueryString(sSQL);
      return command;
   }

   void 
   SQLStatement::AddWhereClauseColumn(const String &sName, const String &sValue)
   {
      Column col;
      col.iType = ColTypeString;
      col.sName = sName;
      col.sString = sValue;

      where_clause_columns_.push_back(col);
   }

   void
   SQLStatement::SetTopRows(int rowCount)
   {
      top_rows_ = rowCount;
   }

   String 
   SQLStatement::GetCreateDatabase(std::shared_ptr<DatabaseSettings> pSettings, const String &sDatabaseName)
   {
      HM::DatabaseSettings::SQLDBType DBType = pSettings->GetType();
      String sSQL;
      switch (DBType)
      {
      case DatabaseSettings::TypeMSSQLServer:
         sSQL.Format(_T("create database %s"), sDatabaseName.c_str());
         break;
      case DatabaseSettings::TypeMYSQLServer:
         sSQL.Format(_T("create database %s character set 'utf8'"), sDatabaseName.c_str());
         break;
      case DatabaseSettings::TypePGServer:
         sSQL.Format(_T("create database \"%s\" ENCODING = 'UTF8'"), sDatabaseName.c_str());
         break;
      default:
         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5407, "SQLStatement::GetCreateDatabase()", Formatter::Format("Unknown database type: {0}", DBType));
         break;
      }

      return sSQL;
   }

   int 
   SQLStatement::GetNoOfCols() const
   { 
      return (int) vecColumns.size(); 
   }

   String 
   SQLStatement::GetCurrentTimestamp()
   {
      HM::DatabaseSettings::SQLDBType DBType = IniFileSettings::Instance()->GetDatabaseType();
      
      switch (DBType)
      {
      case DatabaseSettings::TypeMSSQLServer:
      case DatabaseSettings::TypeMSSQLCompactEdition:
         return "GETDATE()";
      case DatabaseSettings::TypePGServer:
         return "current_timestamp";
      case DatabaseSettings::TypeMYSQLServer:
         return "NOW()";
      default:
         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5407, "SQLStatement::GetCurrentTimestamp()", Formatter::Format("Unknown database type: {0}", DBType));   
      }

      assert(0);
      return "";
   }

   String 
   SQLStatement::GetLeftFunction(const String &sParamName, int iLength)
   {
      HM::DatabaseSettings::SQLDBType DBType = IniFileSettings::Instance()->GetDatabaseType();
      String sRetVal;

      switch (DBType)
      {
      case DatabaseSettings::TypeMSSQLServer:
      case DatabaseSettings::TypeMSSQLCompactEdition:
         /*
            Use SUBSTRING instead of the normal LEFT. LEFT doesn't work
            with MSSQL Compact Edition while SUBSTRING works with both.
         */
         sRetVal.Format(_T("SUBSTRING(%s, 1, %d)"), sParamName.c_str(), iLength);
         break;
      case DatabaseSettings::TypePGServer:
         sRetVal.Format(_T("SUBSTRING(%s FROM 1 FOR %d)"), sParamName.c_str(), iLength);
         break;
      case DatabaseSettings::TypeMYSQLServer:
         sRetVal.Format(_T("LEFT(%s, %d)"), sParamName.c_str(), iLength);
         break;
      default:
         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5407, "SQLStatement::GetLeftFunction()", Formatter::Format("Unknown database type: {0}", DBType));
         break;
      }

      return sRetVal;
   }

   String 
   SQLStatement::GetTopRows(const String &tableName, int rows)
   {
      HM::DatabaseSettings::SQLDBType DBType = IniFileSettings::Instance()->GetDatabaseType();
      String sRetVal;

      switch (DBType)
      {
      case DatabaseSettings::TypeMSSQLServer:
      case DatabaseSettings::TypeMSSQLCompactEdition:
         /*
         Use SUBSTRING instead of the normal LEFT. LEFT doesn't work
         with MSSQL Compact Edition while SUBSTRING works with both.
         */
         sRetVal.Format(_T("SELECT TOP %d FROM %s"), rows, tableName.c_str());
         break;
      case DatabaseSettings::TypePGServer:
         sRetVal.Format(_T("SELECT * FROM %s LIMIT %d"), tableName.c_str(), rows);
         break;
      case DatabaseSettings::TypeMYSQLServer:
         sRetVal.Format(_T("SELECT * FROM %s LIMIT 0, %d"), tableName.c_str(), rows);
         break;
      default:
         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5407, "SQLStatement::GetTopRows()", Formatter::Format("Unknown database type: {0}", DBType));
         break;
      }

      return sRetVal;
   }

   String 
   SQLStatement::GetCurrentTimestampPlusMinutes(int iMinutes)
   {
      HM::DatabaseSettings::SQLDBType DBType = IniFileSettings::Instance()->GetDatabaseType();

      String sRetVal;

      switch (DBType)
      {
      case DatabaseSettings::TypeMYSQLServer:
         sRetVal.Format(_T("DATE_ADD(CONCAT(CURDATE(), ' ', CURTIME()), INTERVAL %d MINUTE)"), iMinutes);
         break;
      case DatabaseSettings::TypeMSSQLServer:
      case DatabaseSettings::TypeMSSQLCompactEdition:
         sRetVal.Format(_T("DATEADD(mi, %d, GETDATE())"), iMinutes);
         break;
      case DatabaseSettings::TypePGServer:
         sRetVal.Format(_T("current_timestamp + INTERVAL '%d minutes'"), iMinutes);
         break;
      default:
         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5407, "SQLStatement::GetCurrentTimestampPlusMinutes()", Formatter::Format("Unknown database type: {0}", DBType));
         break;

      }

      return sRetVal;
   }

   String
   SQLStatement::EscapeLike(const String &input)
   {
      // The escape character first, so that the ones added for the wildcards stay single.
      String result = input;
      result.Replace(_T("/"), _T("//"));
      result.Replace(_T("%"), _T("/%"));
      result.Replace(_T("_"), _T("/_"));
      return result;
   }

   namespace
   {
      bool IsParameterNameCharacter(wchar_t c)
      {
         return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
      }
   }

   bool
   SQLStatement::ToPositionalParameters(const SQLCommand &command, String &positionalQuery,
                                        std::vector<const SQLParameter *> &orderedParameters,
                                        PlaceholderStyle style)
   {
      // For the database clients that bind parameters by position. The query is scanned once,
      // as written, and every parameter name is replaced by a placeholder where it stands.
      // A name is matched as a whole token, @ followed by letters, digits and underscores, so
      // @ID never matches the start of @IDX; anything else, such as @@IDENTITY or an @ that
      // names no parameter, is copied unchanged.
      //
      // Numbered placeholders are PostgreSQL's $1, $2, ...: a parameter used twice gets the
      // same number both times, and the numbers follow the order the names first appear in.
      // MySQL's ? placeholders carry no number, so each one is bound to the next value in
      // turn, and a parameter used twice is listed twice.
      //
      // A bound parameter is a value, so it cannot stand inside a literal of the query, where
      // the server would read $1 as the two characters. Such a query is refused: false, and
      // the caller should not run it.
      const String queryString = command.GetQueryString();
      const std::list<SQLParameter> &parameters = command.GetParameters();

      const std::wstring &query = queryString;

      positionalQuery.clear();
      positionalQuery.reserve(query.size());
      orderedParameters.clear();

      bool insideLiteral = false;

      size_t position = 0;
      while (position < query.size())
      {
         wchar_t c = query[position];

         // '' inside a literal is an escaped quote: it ends the literal and starts it again,
         // which leaves the state as it was.
         if (c == '\'')
            insideLiteral = !insideLiteral;

         if (c != '@')
         {
            positionalQuery += c;
            position++;
            continue;
         }

         size_t end = position + 1;
         while (end < query.size() && IsParameterNameCharacter(query[end]))
            end++;

         const String token = query.substr(position, end - position);

         auto parameter = std::find_if(parameters.begin(), parameters.end(),
            [&token](const SQLParameter &candidate) { return candidate.GetName() == token; });

         if (end == position + 1 || parameter == parameters.end())
            positionalQuery += token;
         else
         {
            if (insideLiteral)
               return false;

            if (style == QuestionMarkPlaceholders)
            {
               orderedParameters.push_back(&*parameter);
               positionalQuery += _T("?");
            }
            else
            {
               auto bound = std::find(orderedParameters.begin(), orderedParameters.end(), &*parameter);
               size_t index = bound - orderedParameters.begin();
               if (bound == orderedParameters.end())
                  orderedParameters.push_back(&*parameter);

               positionalQuery += _T("$") + StringParser::IntToString((int) index + 1);
            }
         }

         position = end;
      }

      return true;
   }

   String
   SQLStatement::ConvertWildcardToLike(String input)
   {
      input.Replace(_T("/"), _T("//"));
      input.Replace(_T("%"), _T("/%"));
      input.Replace(_T("_"), _T("/_"));
      input.Replace(_T("?"), _T("_"));
      input.Replace(_T("*"), _T("%"));
      return input;
   }

   String
   SQLStatement::ConvertLikeToWildcard(String input)
   {
      input.Replace(_T("//"), _T("/"));
      input.Replace(_T("/%"), _T("¤¤¤ESCAPED¤¤¤PERCENTAGE¤¤¤"));
      input.Replace(_T("/_"), _T("¤¤¤ESCAPED¤¤¤UNDERSCORE¤¤¤"));
      input.Replace(_T("_"), _T("?"));
      input.Replace(_T("%"), _T("*"));
      input.Replace(_T("¤¤¤ESCAPED¤¤¤PERCENTAGE¤¤¤"), _T("%"));
      input.Replace(_T("¤¤¤ESCAPED¤¤¤UNDERSCORE¤¤¤"), _T("_"));

      return input;
   }

   namespace
   {
      void AssertPositional(const SQLCommand &command, const String &expectedQuery, const std::vector<AnsiString> &expectedNames,
                            SQLStatement::PlaceholderStyle style = SQLStatement::NumberedPlaceholders)
      {
         String actualQuery;
         std::vector<const SQLParameter *> actualParameters;

         if (!SQLStatement::ToPositionalParameters(command, actualQuery, actualParameters, style))
            throw std::logic_error(("SQLStatement::ToPositionalParameters refused [" + AnsiString(command.GetQueryString()) + "].").c_str());

         if (actualQuery != expectedQuery)
            throw std::logic_error(("SQLStatement::ToPositionalParameters: expected [" + AnsiString(expectedQuery) +
               "], got [" + AnsiString(actualQuery) + "].").c_str());

         bool sameNames = actualParameters.size() == expectedNames.size();
         for (size_t i = 0; sameNames && i < expectedNames.size(); i++)
            sameNames = actualParameters[i]->GetName() == String(expectedNames[i]);

         if (!sameNames)
            throw std::logic_error(("SQLStatement::ToPositionalParameters: wrong parameter order for [" + AnsiString(expectedQuery) + "].").c_str());
      }

      void AssertPositionalRefused(const SQLCommand &command,
                                   SQLStatement::PlaceholderStyle style = SQLStatement::NumberedPlaceholders)
      {
         String query;
         std::vector<const SQLParameter *> parameters;

         if (SQLStatement::ToPositionalParameters(command, query, parameters, style))
            throw std::logic_error(("SQLStatement::ToPositionalParameters accepted a parameter inside a literal: [" +
               AnsiString(command.GetQueryString()) + "].").c_str());
      }
   }

   void
   SQLStatementTester::Test()
   {
      // Positional parameters: numbered in the order the names first appear, a name used
      // twice gets one position, and nothing else in the query changes.
      {
         SQLCommand command(_T("select @@IDENTITY, 'a@b' from t where a = @A and b = @B and c = @A and d = @UNKNOWN"));
         command.AddParameter("@B", 2);
         command.AddParameter("@A", String(_T("x@B")));
         command.AddParameter("@UNUSED", 3);
         AssertPositional(command, _T("select @@IDENTITY, 'a@b' from t where a = $1 and b = $2 and c = $1 and d = @UNKNOWN"), { "@A", "@B" });
      }

      // Names are matched as whole tokens, whatever order the parameters were added in, and
      // an @ that names no parameter is copied unchanged.
      {
         SQLCommand command(_T("values (@ID, @IDX, @ID, @@IDENTITY, @)"));
         command.AddParameter("@IDX", 2);
         command.AddParameter("@ID", 1);
         AssertPositional(command, _T("values ($1, $2, $1, @@IDENTITY, @)"), { "@ID", "@IDX" });
         AssertPositional(command, _T("values (?, ?, ?, @@IDENTITY, @)"), { "@ID", "@IDX", "@ID" },
                          SQLStatement::QuestionMarkPlaceholders);
      }

      // An escaped quote does not end the literal it is in, and a literal that has ended
      // leaves the parameters after it alone.
      {
         SQLCommand command(_T("select * from t where a = 'it''s' and b = @A"));
         command.AddParameter("@A", 1);
         AssertPositional(command, _T("select * from t where a = 'it''s' and b = $1"), { "@A" });
      }

      // A parameter inside a literal cannot be bound, and the query is refused.
      {
         SQLCommand command(_T("update t set d = current_timestamp + INTERVAL '@MINUTES minutes'"));
         command.AddParameter("@MINUTES", 15);
         AssertPositionalRefused(command);

         SQLCommand escaped(_T("select * from t where a = 'it''s @A'"));
         escaped.AddParameter("@A", 1);
         AssertPositionalRefused(escaped);
      }

      // Question marks: one per occurrence, in the order they appear, so a name used twice
      // is listed twice, and nothing else in the query changes.
      {
         SQLCommand command(_T("select @@IDENTITY, 'a@b' from t where a = @A and b = @B and c = @A and d = @UNKNOWN"));
         command.AddParameter("@B", 2);
         command.AddParameter("@A", String(_T("x@B")));
         command.AddParameter("@UNUSED", 3);
         AssertPositional(command, _T("select @@IDENTITY, 'a@b' from t where a = ? and b = ? and c = ? and d = @UNKNOWN"),
                          { "@A", "@B", "@A" }, SQLStatement::QuestionMarkPlaceholders);
      }

      // EscapeLike: the escape character and both wildcards are escaped, the escape character
      // only once, and a backslash - MySQL's default escape - is an ordinary character.
      {
         String escaped = SQLStatement::EscapeLike(_T("a/b%c_d\\e"));
         if (escaped != _T("a//b/%c/_d\\e"))
            throw std::logic_error(("SQLStatement::EscapeLike: got [" + AnsiString(escaped) + "].").c_str());
      }

      // A parameter inside a literal is refused with question marks too.
      {
         SQLCommand command(_T("select * from t where a = 'it''s @A'"));
         command.AddParameter("@A", 1);
         AssertPositionalRefused(command, SQLStatement::QuestionMarkPlaceholders);
      }
   }
}
