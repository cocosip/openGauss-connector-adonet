# openGauss-connector-adonet

#### 介绍

Npgsql 5.0.7 移植版本

#### 贡献

1. 移植ADO.NET Connector项目
2. 重新实现了RFC5802 SHA256认证部分，能够支持SHA256方式登录认证
3. 修改protocolVersion为351
4. 对常用DDL(create table),DML(select,insert,update,delete)均进行了基本测试，测试通过