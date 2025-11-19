// biome-ignore-all lint: old file

const sequelize = require('../../config/config');
const EspecialidadPersonal = require('../../models/EspecialidadPersonal');
const Personal = require('../../models/PERSONAL');
const {
  fechasCitas,
  turnosCitas,
  medicotoCodigo,
} = require('../../helpers/helpers');
const _ = require('lodash');

const EspecialidadfindAll = async (req, res) => {
  try {
    const query = `
      SELECT DISTINCT E.ESPECIALIDAD
      FROM ESPECIALIDADES_MEDICOS ESP
      LEFT JOIN ESPECIALIDADES E ON ESP.ESP_CODIGO = E.CODIGO
      LEFT JOIN PERSONAL P ON ESP.PRS_CODIGO = P.CODIGO
      LEFT JOIN HORARIOS_MEDICO H ON H.PRS_CODIGO = P.CODIGO
      LEFT JOIN ADM_SERVIDORES_DIAGNOSTICO SD ON SD.PRS_CODIGO = P.CODIGO
      WHERE 
        P.ESTADO_DE_DISPONIBILIDAD = 'D' 
        AND E.ESTADO_DE_DISPONIBILIDAD = 'D'
        AND P.PERMITIR_TURNO = 'V'
        AND H.TIPO_TURNO IS NOT NULL
        AND SD.PRS_CODIGO IS NULL
        AND H.TIPO_TURNO = 'P'
        AND E.NIVEL_CONSULTA <4 
      ORDER BY E.ESPECIALIDAD;
    `;

    const especialidades = await sequelize.query(query, {
      replacements: {},
      type: sequelize.QueryTypes.SELECT,
    });

    const formattedEspecialidades = _.map(especialidades, item => {
      return _.update(item, 'ESPECIALIDAD', value => {
        if (value === 'Traumatología y Ort') return 'Traumatología';
        if (value === 'Otorrinolaringología') return 'Otorrino';
        return value;
      });
    });

    res.json(formattedEspecialidades);
    console.log(formattedEspecialidades);
  } catch (error) {
    console.error('Error fetching especialidades:', error);
    res.status(500).json({
      error: `An error occurred while fetching especialidades: ${error.message}`,
    });
  }
};

const findMedicoEspecialidad = async (req, res) => {
  let { Especialidad } = req.params;

  Especialidad = decodeURIComponent(Especialidad);

  if (Especialidad === 'Traumatología') {
    Especialidad = 'Traumatología y Ort';
  }

  if (Especialidad === 'Otorrino') {
    Especialidad = 'Otorrinolaringología';
  }
  try {
    const query = `SELECT DISTINCT 
      CASE 
          WHEN P.SEXO = 'M' THEN 'Dr. ' || SUBSTR(P.NOMBRES, 1, 1) || '. ' || SUBSTR(P.APELLIDOS, 1, INSTR(P.APELLIDOS, ' ') - 1)
          WHEN P.SEXO = 'F' THEN 'Dra. ' || SUBSTR(P.NOMBRES, 1, 1) || '. ' || SUBSTR(P.APELLIDOS, 1, INSTR(P.APELLIDOS, ' ') - 1)
          ELSE SUBSTR(P.NOMBRES, 1, 1) || '. ' || SUBSTR(P.APELLIDOS, 1, INSTR(P.APELLIDOS, ' ') - 1)
      END AS NOMBRE_COMPLETO
  FROM 
      ESPECIALIDADES_MEDICOS ESP
      LEFT JOIN ESPECIALIDADES E ON ESP.ESP_CODIGO = E.CODIGO
      LEFT JOIN PERSONAL P ON ESP.PRS_CODIGO = P.CODIGO
      LEFT JOIN HORARIOS_MEDICO H ON H.PRS_CODIGO = P.CODIGO
      LEFT JOIN ADM_SERVIDORES_DIAGNOSTICO SD ON SD.PRS_CODIGO = P.CODIGO
  WHERE 
      P.ESTADO_DE_DISPONIBILIDAD = 'D' 
      AND E.ESTADO_DE_DISPONIBILIDAD = 'D'
      AND P.PERMITIR_TURNO = 'V'
      AND H.TIPO_TURNO IS NOT NULL
      AND SD.PRS_CODIGO IS NULL
      AND H.TIPO_TURNO = 'P'
      AND E.ESPECIALIDAD = :Especialidad
  ORDER BY 
      NOMBRE_COMPLETO;`;

    const Medicos = await sequelize.query(query, {
      replacements: {
        Especialidad: Especialidad,
      },
      type: sequelize.QueryTypes.SELECT,
    });

    if (_.isEmpty(Medicos)) {
      return res
        .status(404)
        .json({ error: 'No existen médicos para su selección' });
    }

    console.log(Medicos);
    res.json(Medicos);
  } catch (error) {
    console.error('Error fetching personal:', error);
    res.status(500).json({ error: `An error occurred: ${error.message}` });
  }
};

const findFechasMedico = async (req, res) => {
  console.log(req.body);
  let { Medico, Especialidad } = req.body;
  if (Especialidad === 'Traumatología') {
    Especialidad = 'Traumatología y Ort';
  }

  if (Especialidad === 'Otorrino') {
    Especialidad = 'Otorrinolaringología';
  }
  try {
    const response = await fechasCitas(Medico, Especialidad);
    const fechas = response.fechas || [];
    console.log(fechas);
    return res.send(fechas);
  } catch (error) {
    console.error('Error fetching personal:', error);
    res.status(500).json({ error: `An error occurred: ${error.message}` });
  }
};
const findHorasCitas = async (req, res) => {
  let { Medico, Especialidad, Fecha } = req.body;
  if (Especialidad === 'Traumatología') {
    Especialidad = 'Traumatología y Ort';
  }
  if (Especialidad === 'Otorrino') {
    Especialidad = 'Otorrinolaringología';
  }
  try {
    const response = await turnosCitas(Medico, Especialidad, Fecha);

    if (response.error) {
      return res.status(404).json({ error: response.error });
    }

    res.status(200).json(response);
  } catch (error) {
    console.error('Error fetching appointment times:', error);
    res.status(500).json({ error: `An error occurred: ${error.message}` });
  }
};
const citasAgendadas = async (req, res) => {
  const { cedula } = req.params;

  try {
    const query = `
      SELECT DISTINCT 
        P.APELLIDOS || ' ' || P.NOMBRES AS NOMBRE, 
        E.ESPECIALIDAD, 
        TO_CHAR(TU.CREADO, 'DD/MM/YYYY') AS FECHA, 
        TO_CHAR(TU.CREADO, 'HH24:MI:SS') AS HORA, 
        TU.NUMERO,
        TU.CREADO 
      FROM 
        TURNOS_CE TU
        LEFT JOIN PERSONAL P ON TU.PRS_CODIGO = P.CODIGO
        LEFT JOIN ESPECIALIDADES_MEDICOS ESP ON P.CODIGO = ESP.PRS_CODIGO
        LEFT JOIN ESPECIALIDADES E ON E.CODIGO = ESP.ESP_CODIGO
      WHERE 
        TU.PCN_NUMERO_HC = (SELECT NUMERO_HC FROM PACIENTES WHERE CEDULA = :cedula)
        AND TU.CREADO >= SYSDATE 
        AND TU.ESTADO IN ('P', 'R')
      ORDER BY 
        TU.CREADO;
    `;

    const citas = await sequelize.query(query, {
      replacements: { cedula: cedula },
      type: sequelize.QueryTypes.SELECT,
    });

    if (_.isEmpty(citas)) {
      return res
        .status(404)
        .json({ error: 'No appointments found for the specified ID' });
    }

    const formattedCitas = _.map(citas, cita => ({
      NOMBRE: _.startCase(_.toLower(cita.NOMBRE)),
      ESPECIALIDAD: _.startCase(_.toLower(cita.ESPECIALIDAD)),
      FECHA: cita.FECHA,
      TURNO: cita.HORA,
    }));

    res.json(formattedCitas);
    console.log(formattedCitas);
  } catch (error) {
    console.error('Error fetching appointment times:', error);
    res.status(500).json({ error: `An error occurred: ${error.message}` });
  }
};

const valorCita = async (req, res) => {
  let { Medico, Especialidad } = req.body;
  if (Especialidad === 'Traumatología') {
    Especialidad = 'Traumatología y Ort';
  }

  if (Especialidad === 'Otorrino') {
    Especialidad = 'Otorrinolaringología';
  }
  try {
    const { codigo, error } = await medicotoCodigo(Especialidad, Medico);
    const query = `SELECT calcula_valor_consulta(:codigo, '06', 'C', '0', 'N') AS VALOR FROM DUAL;`;
    const [results] = await sequelize.query(query, {
      replacements: {
        codigo: codigo,
      },
    });

    const valor = results[0]?.VALOR;

    if (_.isEmpty(valor) || !valor.includes('/')) {
      return res.status(404).json({
        mensaje:
          'El formato del valor de la consulta es incorrecto o está vacío',
      });
    }

    const PRECIO = valor.split('/')[1];

    if (_.isNaN(Number(PRECIO))) {
      return res
        .status(404)
        .json({ mensaje: 'El valor del precio no es numérico' });
    }
    console.log({ PRECIO });
    return res.json({ PRECIO });
  } catch (error) {
    console.error(error);
    return res
      .status(500)
      .json({ mensaje: 'Error al calcular el valor de la consulta' });
  }
};

const reagendarCita = async (req, res) => {
  const { cedula, turno, fecha, hora, medico } = req.body;
  try {
    const sql = `SELECT * FROM TURNOS_CE WHERE PCN_NUMERO_HC = (SELECT NUMERO_HC FROM PACIENTES WHERE CEDULA = :cedula) AND CREADO >= SYSDATE AND ESTADO IN ('P', 'R') AND NUMERO = :turno`;
    const [turnoExistente] = await sequelize.query(sql, {
      replacements: { cedula, turno },
      type: sequelize.QueryTypes.SELECT,
    });
  } catch (error) {
    console.error('Error al reagendar cita:', error);
    return res.status(500).json({ mensaje: 'Error al reagendar cita' });
  }
};

module.exports = {
  EspecialidadfindAll,
  findMedicoEspecialidad,
  findFechasMedico,
  findHorasCitas,
  citasAgendadas,
  valorCita,
};
