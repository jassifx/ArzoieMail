// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

#include "DALRecordset.h"
#include "MySQLInterface.h"
#include "MySQLStatement.h"

namespace HM
{

   class ColumnPositions;

   class MySQLRecordset  : public DALRecordset
   {
   public:
	   MySQLRecordset();
	   virtual ~MySQLRecordset();

      virtual DALConnection::ExecutionResult TryOpen(std::shared_ptr<DALConnection> pDALConn, const SQLCommand &command, String &sErrorMessage);
      
      virtual bool MoveNext();
      virtual bool IsEOF() const;
      virtual long RecordCount() const; 

      virtual long GetLongValue(const AnsiString &FieldName) const;
      virtual String GetStringValue(const AnsiString &FieldName) const;
      virtual __int64 GetInt64Value(const AnsiString &FieldName) const;
      virtual double GetDoubleValue(const AnsiString &FieldName) const;

      std::vector<AnsiString> GetColumnNames() const;

      virtual bool GetIsNull(const AnsiString &FieldName) const;

   private:

      int GetColumnIndex_(const AnsiString &sColumnName) const;

      

      void Close_();

      // A query's rows stay in the client's result. A prepared statement's are read into
      // statement_rows_, and current_ points at the values of the row it is on, so that
      // both are read the same way.
      hm_MYSQL_RES *result_;
      hm_MYSQL_ROW current_;

      bool from_statement_;
      MySQLStatement::Rows statement_rows_;
      size_t next_statement_row_;
      std::vector<char *> current_statement_row_;

      std::shared_ptr<ColumnPositions> column_positions_;
   };
}
