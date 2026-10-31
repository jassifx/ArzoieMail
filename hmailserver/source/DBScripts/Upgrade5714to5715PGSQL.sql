create table hm_bayes_tokens (tokenhash bigint not null primary key, spamcount int not null, hamcount int not null);

insert into hm_bayes_tokens (tokenhash, spamcount, hamcount) values (0, 0, 0);

insert into hm_settings (settingname, settingstring, settinginteger) values ('bayesenabled', '', 1);

insert into hm_settings (settingname, settingstring, settinginteger) values ('bayesscore', '', 5);

insert into hm_settings (settingname, settingstring, settinginteger) values ('bayesthreshold', '', 90);

insert into hm_settings (settingname, settingstring, settinginteger) values ('bayesminimummessages', '', 200);

insert into hm_settings (settingname, settingstring, settinginteger) values ('bayeslearnfromjunk', '', 1);

update hm_dbversion set value = 5715;