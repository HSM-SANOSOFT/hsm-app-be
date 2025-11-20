
--- DDL START ---

create table PERSONAL

(
CODIGO    VARCHAR2(4) not null,
ESPPRS_CODIGO    VARCHAR2(3) not null,
APELLIDOS    VARCHAR2(30) not null,
NOMBRES    VARCHAR2(25) not null,
ESTADO_DE_DISPONIBILIDAD    CHAR default 'D' not null,
CEDULA    VARCHAR2(10) not null,
CARGO    VARCHAR2(3) not null,
TELEFONO    VARCHAR2(24),
DIRECCION    VARCHAR2(200),
NUMERO_CMA    VARCHAR2(5),
USUARIO    VARCHAR2(30) not null,
PERMITIR_TURNO    VARCHAR2(1) default 'F' not null,
PERSONAL_CIRUGIA    CHAR default 'F' not null,
BENEFICIARIO    CHAR default 'F' not null,
LIBRO_MSP    VARCHAR2(20),
FOLIO_MSP    VARCHAR2(20),
NUMERO_MSP    VARCHAR2(20),
AREA_FISICA_ASIGNADA    VARCHAR2(1) not null,
DEPARTAMENTO_FISICO_ASIGNADO    VARCHAR2(2) not null,
EMAIL    VARCHAR2(360) not null,
FIRMA_INICIALES    BLOB,
FIRMA_RUBRICA    LONG,
SELLO    BLOB,
FIRMA_Y_SELLO    BLOB,
FIRMA_Y_SELLO_H    BLOB,
SEXO    VARCHAR2(1) not null,
SENESCYT    VARCHAR2(20),
MIEMBRO_STAFF    CHAR default 'N' not null,
PASS_CERT    VARCHAR2(20),
UBICACION    VARCHAR2(3) default 'C01',
ATENCION_HOSPITALARIA    VARCHAR2(1) default 'F' not null,
PASSWORD_HASH    CLOB,
NOMINA    VARCHAR2(1) default 'N' not null,
NUMERO_SSA    NUMBER default 1 not null,
FECHA    DATE,
CLASE_MEDICO    VARCHAR2(1) default 'N',
constraint PRS_PK
        primary key (CODIGO),
constraint PRS_UK
        unique (USUARIO),
constraint PRS_DEPARTAMENTO_FK
        foreign key (AREA_FISICA_ASIGNADA, DEPARTAMENTO_FISICO_ASIGNADO) references DEPARTAMENTOS,
constraint PRS_SSA_FK
        foreign key (NUMERO_SSA) references SSA_PUESTOS_TRABAJO,
constraint PRS_AREA_FK
        foreign key (AREA_FISICA_ASIGNADA) references AREAS,
constraint PRS_ESPPRS_FK
        foreign key (ESPPRS_CODIGO) references ESPECIALIDAD_PERSONAL
)

/
comment on column PERSONAL.CODIGO is 'Código del personal';
comment on column PERSONAL.ESPPRS_CODIGO is 'Código del cargo';
comment on column PERSONAL.APELLIDOS is 'Apellidos';
comment on column PERSONAL.NOMBRES is 'Nombres';
comment on column PERSONAL.ESTADO_DE_DISPONIBILIDAD is 'Si esta disponible, fuera de servicio, etc';
comment on column PERSONAL.CEDULA is 'Cédula de identidad';
comment on column PERSONAL.CARGO is 'Cárgo del personal EN CG_REF_CODES C WHERE C.RV_DOMAIN=CARGO';
comment on column PERSONAL.TELEFONO is 'Teléfonos del médico';
comment on column PERSONAL.DIRECCION is 'Dirección domiciliaria';
comment on column PERSONAL.NUMERO_CMA is 'Núm. Afil. Colegio Médico';
comment on column PERSONAL.USUARIO is 'Nombre de usuario de la BD';
comment on column PERSONAL.LIBRO_MSP is 'POR ELIMINAR';
comment on column PERSONAL.FOLIO_MSP is 'POR ELIMINAR';
comment on column PERSONAL.NUMERO_MSP is 'POR ELIMINAR';
comment on column PERSONAL.AREA_FISICA_ASIGNADA is 'codigo de la tabla AREA ';
comment on column PERSONAL.DEPARTAMENTO_FISICO_ASIGNADO is 'Codigo del Departamento';
comment on column PERSONAL.EMAIL is 'MAIL DEL MEDICO';
comment on column PERSONAL.FIRMA_INICIALES is 'El visto bueno ';
comment on column PERSONAL.UBICACION is 'Cubiculo de atencion donde atiende ';
comment on column PERSONAL.ATENCION_HOSPITALARIA is 'V DISPONIBLE Y F NO DISPONIBLE';
comment on column PERSONAL.PASSWORD_HASH is 'hash de contraseña para migracion a postgresql, uso metodo bcrypt, funcion password_hash(pass) php, password_verify($inputPassword, $hashedPassword)';
comment on column PERSONAL.NOMINA is 'Si constan en la nomina de las empresas, n si tiene la hc activa o certificados significa que son servicios prestado';
comment on column PERSONAL.FECHA is 'FECHA DE CUANDO SE CREO EL USUARIO';
comment on column PERSONAL.CLASE_MEDICO is 'P PODEMOS COMPARTIR HONORARIOS, N NO PODEMOS MEDICO ES INDEPENDIENTE';

---  DDL END  ---
