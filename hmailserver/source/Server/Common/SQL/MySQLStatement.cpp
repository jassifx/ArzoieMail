// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#include "stdafx.h"

#include "MySQLStatement.h"
#include "SQLCommand.h"
#include "SQLParameter.h"
#include "SQLStatement.h"

#include "../Util/Unicode.h"

// The MariaDB Connector/C headers, for MYSQL_BIND, MYSQL_FIELD and the type and attribute
// values. Nothing is linked against them: the functions come through MySQLInterface.
#include <mysql.h>

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   namespace
   {
      // The size of a column buffer when the server reports a smaller maximum length. A value
      // longer than its buffer is fetched again in full, so this only saves that second fetch
      // for the short values that make up most columns.
      const unsigned long MinimumColumnBuffer = 64;
   }

   MySQLStatement::MySQLStatement(hm_MYSQL *connection) :
      connection_(connection),
      statement_(nullptr),
      error_number_(0)
   {

   }

   MySQLStatement::~MySQLStatement()
   {
      try
      {
         if (statement_)
            MySQLInterface::Instance()->p_mysql_stmt_close(statement_);
      }
      catch (...)
      {

      }
   }

   bool
   MySQLStatement::Fail_(const char *step, String &errorMessage)
   {
      MySQLInterface *client = MySQLInterface::Instance();

      const char *error = nullptr;
      if (statement_)
      {
         error_number_ = client->p_mysql_stmt_errno(statement_);
         error = client->p_mysql_stmt_error(statement_);
      }
      else
      {
         error_number_ = client->p_mysql_errno(connection_);
         error = client->p_mysql_error(connection_);
      }

      String description;
      if (error)
         Unicode::MultiByteToWide(error, description);

      errorMessage.Format(_T("%hs: %s"), step, description.c_str());
      return false;
   }

   bool
   MySQLStatement::Execute(const SQLCommand &command, Rows *rows, __int64 *insertId, String &errorMessage)
   {
      MySQLInterface *client = MySQLInterface::Instance();
      error_number_ = 0;

      String positionalQuery;
      std::vector<const SQLParameter *> parameters;
      if (!SQLStatement::ToPositionalParameters(command, positionalQuery, parameters, SQLStatement::QuestionMarkPlaceholders))
      {
         errorMessage = _T("A parameter of the query stands inside a literal, where it cannot be bound: ") + command.GetQueryString();
         return false;
      }

      AnsiString query;
      if (!Unicode::WideToMultiByte(positionalQuery, query))
      {
         errorMessage = _T("Could not convert the query into UTF-8: ") + command.GetQueryString();
         return false;
      }

      statement_ = client->p_mysql_stmt_init(connection_);
      if (!statement_)
         return Fail_("mysql_stmt_init", errorMessage);

      if (client->p_mysql_stmt_prepare(statement_, query.c_str(), (unsigned long) query.size()) != 0)
         return Fail_("mysql_stmt_prepare", errorMessage);

      // The values the binds point at. Sized before any bind takes a pointer into them, so
      // that none moves afterwards.
      std::vector<MYSQL_BIND> binds(parameters.size());
      std::vector<long long> numbers(parameters.size());
      std::vector<AnsiString> strings(parameters.size());
      std::vector<unsigned long> lengths(parameters.size());

      for (size_t i = 0; i < parameters.size(); i++)
      {
         const SQLParameter &parameter = *parameters[i];
         MYSQL_BIND &bind = binds[i];
         memset(&bind, 0, sizeof(bind));

         switch (parameter.GetType())
         {
         case SQLParameter::ParamTypeInt32:
            numbers[i] = parameter.GetInt32Value();
            break;
         case SQLParameter::ParamTypeInt64:
            numbers[i] = parameter.GetInt64Value();
            break;
         case SQLParameter::ParamTypeUnsignedInt32:
            numbers[i] = parameter.GetUnsignedInt32Value();
            break;
         case SQLParameter::ParamTypeString:
            if (!Unicode::WideToMultiByte(parameter.GetStringValue(), strings[i]))
            {
               errorMessage = _T("Could not convert the value of ") + parameter.GetName() + _T(" into UTF-8.");
               return false;
            }
            break;
         default:
            errorMessage = _T("The parameter ") + parameter.GetName() + _T(" has a type that cannot be bound.");
            return false;
         }

         if (parameter.GetType() == SQLParameter::ParamTypeString)
         {
            // The connection's character set is utf8mb4 (or utf8), set by SET NAMES when it
            // was opened, so the server reads the value as the UTF-8 it is.
            lengths[i] = (unsigned long) strings[i].size();
            bind.buffer_type = MYSQL_TYPE_STRING;
            bind.buffer = (void *) strings[i].data();
            bind.buffer_length = lengths[i];
            bind.length = &lengths[i];
         }
         else
         {
            // All three integer types fit a signed 64-bit integer.
            bind.buffer_type = MYSQL_TYPE_LONGLONG;
            bind.buffer = &numbers[i];
         }
      }

      if (!binds.empty() && client->p_mysql_stmt_bind_param(statement_, binds.data()) != 0)
         return Fail_("mysql_stmt_bind_param", errorMessage);

      if (client->p_mysql_stmt_execute(statement_) != 0)
         return Fail_("mysql_stmt_execute", errorMessage);

      if (insertId)
         *insertId = (__int64) client->p_mysql_stmt_insert_id(statement_);

      // A statement that returns no rows, or a caller that wants none: the rows the server
      // sends are discarded when the statement is closed.
      if (!rows)
         return true;

      return ReadRows_(*rows, errorMessage);
   }

   bool
   MySQLStatement::ReadRows_(Rows &rows, String &errorMessage)
   {
      MySQLInterface *client = MySQLInterface::Instance();

      rows.columns.clear();
      rows.values.clear();

      hm_MYSQL_RES *metadata = client->p_mysql_stmt_result_metadata(statement_);
      if (!metadata)
      {
         // No result set: an insert, update or delete.
         return true;
      }

      // The whole result is read into the client, as mysql_store_result does for a query,
      // with the length of the longest value of each column, so that the buffers can be
      // sized for them.
      my_bool updateMaxLength = 1;
      client->p_mysql_stmt_attr_set(statement_, STMT_ATTR_UPDATE_MAX_LENGTH, &updateMaxLength);

      if (client->p_mysql_stmt_store_result(statement_) != 0)
      {
         client->p_mysql_free_result(metadata);
         return Fail_("mysql_stmt_store_result", errorMessage);
      }

      unsigned int columnCount = client->p_mysql_num_fields(metadata);

      // Every column is fetched as a string, which the client converts numbers and dates
      // into, so the recordset reads them as it reads the rows of a query.
      std::vector<MYSQL_BIND> binds(columnCount);
      std::vector<std::vector<char>> buffers(columnCount);
      std::vector<unsigned long> lengths(columnCount);
      std::vector<my_bool> nulls(columnCount);
      std::vector<my_bool> truncated(columnCount);

      for (unsigned int column = 0; column < columnCount; column++)
      {
         MYSQL_FIELD *field = (MYSQL_FIELD *) client->p_mysql_fetch_field_direct(metadata, column);
         rows.columns.push_back(field->name);

         buffers[column].resize(std::max(field->max_length, MinimumColumnBuffer) + 1);

         MYSQL_BIND &bind = binds[column];
         memset(&bind, 0, sizeof(bind));
         bind.buffer_type = MYSQL_TYPE_STRING;
         bind.buffer = buffers[column].data();
         bind.buffer_length = (unsigned long) buffers[column].size();
         bind.length = &lengths[column];
         bind.is_null = &nulls[column];
         bind.error = &truncated[column];
      }

      client->p_mysql_free_result(metadata);

      if (columnCount > 0 && client->p_mysql_stmt_bind_result(statement_, binds.data()) != 0)
         return Fail_("mysql_stmt_bind_result", errorMessage);

      for (;;)
      {
         std::fill(truncated.begin(), truncated.end(), (my_bool) 0);

         int fetched = client->p_mysql_stmt_fetch(statement_);
         if (fetched == MYSQL_NO_DATA)
            break;

         if (fetched != 0 && fetched != MYSQL_DATA_TRUNCATED)
            return Fail_("mysql_stmt_fetch", errorMessage);

         std::vector<std::optional<std::string>> row(columnCount);

         for (unsigned int column = 0; column < columnCount; column++)
         {
            if (nulls[column])
               continue;

            if (!truncated[column] && lengths[column] < buffers[column].size())
            {
               row[column] = std::string(buffers[column].data(), lengths[column]);
               continue;
            }

            // Longer than its buffer: lengths holds the full length, and the value is
            // fetched again into a buffer that holds it.
            std::string value(lengths[column], '\0');
            unsigned long length = 0;

            MYSQL_BIND full;
            memset(&full, 0, sizeof(full));
            full.buffer_type = MYSQL_TYPE_STRING;
            full.buffer = value.data();
            full.buffer_length = (unsigned long) value.size();
            full.length = &length;

            if (client->p_mysql_stmt_fetch_column(statement_, &full, column, 0) != 0)
               return Fail_("mysql_stmt_fetch_column", errorMessage);

            value.resize(std::min<size_t>(length, value.size()));
            row[column] = std::move(value);
         }

         rows.values.push_back(std::move(row));
      }

      return true;
   }
}
