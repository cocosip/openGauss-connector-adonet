# openGauss-connector-adonet

#### 介绍

OpenGauss的Npgsql3.2.7移植版

#### 参与贡献

1.  移植ADO.NET Connector项目
2.  重新实现了RFC5802 SHA256认证部分，能够支持SHA256方式登录认证（借鉴官方JAVA版本postgresql.jar的实现）
3.  修改protocolVersion为351，临时解决 UNLISTEN,DISCARD 不支持的问题，以及加载数据库类型时遇到的问题
4.  对常用DDL(create table),DML(select,insert,update,delete)均进行了基本测试，测试通过

