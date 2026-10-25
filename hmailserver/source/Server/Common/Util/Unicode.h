// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

namespace HM
{
   class Unicode  
   {
   public:
	   Unicode();
	   virtual ~Unicode();

      static AnsiString ToANSI(const String &sString);
      static bool WideToMultiByte(const String &sInput, AnsiString &sOutput);
      static bool MultiByteToWide(const AnsiString &sInput, String &sOutput);

      // The UTF-8 bytes of the string, exactly - without the terminating null WideToMultiByte
      // leaves in what it returns. Empty if the string cannot be converted.
      static AnsiString ToUTF8(const String &sInput);

      static unsigned char* CharMoveNext(unsigned char*, bool utf8);
   private:

   };


}