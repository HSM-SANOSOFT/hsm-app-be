import { CitasTurnoTemplateDto } from '@hsm-lib/definitions/dtos';

export function CitasTurnoTemplate(
  data: CitasTurnoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        {' '}
        Estimad@ paciente: <strong>{data.patientName}</strong>
      </p>
      <p> Le saludamos del Hospital Santamaria, para recordarle su cita: </p>
      <p>
        {' '}
        El dia: <strong>{data.appointmentDate}</strong>{' '}
      </p>
      <p>
        {' '}
        Hora: <strong>{data.appointmentTime}</strong>{' '}
      </p>
      <p>
        {' '}
        Turno: <strong>{data.appointmentDetails}</strong>{' '}
      </p>
      <p>
        {' '}
        Especialidad: <strong>{data.medicalSpecialty}</strong>{' '}
      </p>
      <p>
        {' '}
        <strong>{data.appointmentType}</strong>.
      </p>
      <p>
        Nota: <strong>{data.appointmentNote}</strong>
      </p>
      <p>
        Agradecemos su confianza y quedamos a su disposición para cualquier
        consulta adicional, escriba al numero 0980892296 de nuestro asistente
        virtual SANTI.
      </p>
      <p>Atentamente,</p>
      <p>Servicio al Paciente</p>
      <p>Celular 0981764904</p>
    </>
  );
}
