// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once
#include <atomic>

namespace HM
{
   class Property
   {
   public:
      Property(void);
      ~Property(void);

      Property(const String &sName, long lValue, const String &sValue, bool SaveCrypted = false);
   

      AnsiString GetName() const {return name_;}
      void SetName(const AnsiString &NewVal) {name_ = NewVal;}

      int GetLongValue() const {return long_value_; }
      void SetLongValue(long NewVal);

      String GetStringValue() const
      {
         boost::lock_guard<boost::recursive_mutex> guard(value_mutex_);
         return string_value_;
      }
      void SetStringValue(const String &NewVal);
      void SetStringValueWithoutSaving(const String &NewVal)
      {
         boost::lock_guard<boost::recursive_mutex> guard(value_mutex_);
         string_value_ = NewVal;
      }
      void SetLongValueWithoutSaving(long value) {long_value_ = value; }
      // For a value which has just been read back out of the database, and so does not
      // need writing to it again.

      bool GetBoolValue() const {return long_value_ ? true : false; }
      void SetBoolValue(bool NewVal);

      void SetIsCrypted() {save_crypted_ = true; }
   private:

  
      bool WriteBoolSetting_(bool bValue);
      bool WriteLongSetting_(long lValue);
      bool WriteStringSetting_(const String &sValue);

      std::atomic<long> long_value_;
      mutable boost::recursive_mutex value_mutex_;
      String string_value_;
      AnsiString name_;

      bool save_crypted_;

   };
}
